using System.Collections.ObjectModel;
using System.Dynamic;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Skua.Core.Interfaces;

namespace Skua.Linux.Services;

public partial class PotionEntry : ObservableObject
{
    public string DisplayName { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string[] AuraAliases { get; init; } = Array.Empty<string>();
    public string StatBonus { get; init; } = "";

    [ObservableProperty]
    private bool _enabled;
}

public sealed class AutoCombatPotionService : IDisposable
{
    private readonly IScriptInterface _bot;
    private readonly ILogService _logger;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;
    public bool AutoPullFromBank { get; set; } = true;
    public bool OnlyInCombat { get; set; } = false;

    public ObservableCollection<PotionEntry> Potions { get; } = new();

    public event Action<bool>? StateChanged;

    public AutoCombatPotionService(IScriptInterface bot, ILogService logger)
    {
        _bot = bot;
        _logger = logger;

        InitDefaultPotions();
    }

    private void InitDefaultPotions()
    {
        Potions.Add(new PotionEntry
        {
            DisplayName = "Fate Tonic",
            ItemName = "Fate Tonic",
            AuraAliases = new[] { "Fate", "Fate Tonic" },
            StatBonus = "+LUK / Crit"
        });

        Potions.Add(new PotionEntry
        {
            DisplayName = "Potent Battle Elixir",
            ItemName = "Potent Battle Elixir",
            AuraAliases = new[] { "Battle Elixir", "Potent Battle Elixir" },
            StatBonus = "+Phys Dmg / Crit Dmg"
        });

        Potions.Add(new PotionEntry
        {
            DisplayName = "Potent Malevolence Elixir",
            ItemName = "Potent Malevolence Elixir",
            AuraAliases = new[] { "Malevolence", "Potent Malevolence Elixir" },
            StatBonus = "+Magic Dmg"
        });

        Potions.Add(new PotionEntry
        {
            DisplayName = "Potent Honor Potion",
            ItemName = "Potent Honor Potion",
            AuraAliases = new[] { "Honor Potion", "Potent Honor Potion" },
            StatBonus = "+50% Dmg Burst"
        });

        Potions.Add(new PotionEntry
        {
            DisplayName = "Felicitous Philtre",
            ItemName = "Felicitous Philtre",
            AuraAliases = new[] { "Felicitous Philtre" },
            StatBonus = "+50% Crit Chance"
        });

        Potions.Add(new PotionEntry
        {
            DisplayName = "Body Tonic",
            ItemName = "Body Tonic",
            AuraAliases = new[] { "Body", "Body Tonic" },
            StatBonus = "+END / Max HP"
        });

        Potions.Add(new PotionEntry
        {
            DisplayName = "Sage Tonic",
            ItemName = "Sage Tonic",
            AuraAliases = new[] { "Sage", "Sage Tonic" },
            StatBonus = "+INT / Spell Power"
        });

        Potions.Add(new PotionEntry
        {
            DisplayName = "Potent Revitalize Potion",
            ItemName = "Potent Revitalize Potion",
            AuraAliases = new[] { "Revitalize", "Potent Revitalize Potion" },
            StatBonus = "+HoT Regen"
        });
    }

    public void AddCustomPotion(string itemName, string auraName, string bonus = "Custom")
    {
        if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(auraName))
            return;

        if (Potions.Any(p => p.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase)))
            return;

        Potions.Add(new PotionEntry
        {
            DisplayName = itemName,
            ItemName = itemName,
            AuraAliases = new[] { auraName },
            StatBonus = bonus,
            Enabled = true
        });
    }

    public void Start()
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => WorkerLoopAsync(_cts.Token));
        StateChanged?.Invoke(true);
        _logger.ScriptLog("[AutoPotion] Serviço de Auto Combat Potions iniciado!");
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        StateChanged?.Invoke(false);
        _logger.ScriptLog("[AutoPotion] Serviço de Auto Combat Potions parado.");
    }

    public void Toggle()
    {
        if (IsRunning)
            Stop();
        else
            Start();
    }

    private async Task WorkerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(3500, token);

                if (!_bot.Player.LoggedIn || !_bot.Player.Alive)
                    continue;

                if (OnlyInCombat && !_bot.Player.InCombat)
                    continue;

                List<PotionEntry> activePotions = Potions.Where(p => p.Enabled).ToList();
                if (activePotions.Count == 0)
                    continue;

                foreach (PotionEntry potion in activePotions)
                {
                    if (token.IsCancellationRequested)
                        break;

                    bool hasAura = false;
                    foreach (string aura in potion.AuraAliases)
                    {
                        if (_bot.Self.HasActiveAura(aura))
                        {
                            hasAura = true;
                            break;
                        }
                    }

                    if (hasAura)
                        continue;

                    // Aura is expired or not active, try to apply potion
                    bool hasItem = _bot.Inventory.Contains(potion.ItemName);
                    if (!hasItem && AutoPullFromBank)
                    {
                        if (_bot.Bank.Contains(potion.ItemName))
                        {
                            _bot.Bank.ToInventory(potion.ItemName);
                            await Task.Delay(400, token);
                            hasItem = _bot.Inventory.Contains(potion.ItemName);
                        }
                    }

                    if (!hasItem)
                        continue;

                    // Equip into potion slot
                    _bot.Inventory.EquipUsableItem(potion.ItemName);
                    await Task.Delay(400, token);

                    // Use skill 5 (Potion Slot) using dedicated AS3 usePotion and fallback to UseSkill(5)
                    try
                    {
                        _bot.Flash.Call("usePotion");
                        _bot.Skills.UseSkill(5);

                        bool applied = false;
                        for (int attempt = 0; attempt < 4; attempt++)
                        {
                            await Task.Delay(300, token);
                            if (potion.AuraAliases.Any(a => _bot.Self.HasActiveAura(a)))
                            {
                                applied = true;
                                break;
                            }
                            if (attempt == 1)
                            {
                                _bot.Flash.Call("usePotion");
                                _bot.Skills.UseSkill(5);
                            }
                        }

                        if (applied)
                        {
                            _logger.ScriptLog($"[AutoPotion] Usou '{potion.ItemName}' com sucesso (Aura ativa)!");
                        }
                        else
                        {
                            _logger.ScriptLog($"[AutoPotion] Posição atualizada para '{potion.ItemName}', aguardando ativação...");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.ScriptLog($"[AutoPotion] Falha ao usar '{potion.ItemName}': {ex.Message}");
                    }

                    // Delay between multiple potion uses so the client doesn't choke
                    await Task.Delay(1200, token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ScriptLog($"[AutoPotion Loop Error] {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
