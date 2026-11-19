using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Utils;

namespace Skua.Linux.Services;

public enum QueueItemStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

public partial class ScriptQueueItem : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _scriptPath = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _className = string.Empty;

    [ObservableProperty]
    private bool _enableSmartEnhance;

    [ObservableProperty]
    private int _delaySeconds = 3;

    [ObservableProperty]
    private QueueItemStatus _status = QueueItemStatus.Pending;

    [ObservableProperty]
    private string _statusMessage = "Pendente";
}

public class ScriptQueuePreset
{
    public string Name { get; set; } = string.Empty;
    public bool AutoLogoutWhenFinished { get; set; }
    public bool AutoCloseWhenFinished { get; set; }
    public List<ScriptQueueItem> Items { get; set; } = new();
}

public sealed partial class ScriptQueueService : ObservableObject, IDisposable
{
    private readonly IScriptInterface _bot;
    private readonly ILogService _logger;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _currentScriptTcs;
    private readonly string _presetsDirectory;

    public ObservableCollection<ScriptQueueItem> Items { get; } = new();
    public ObservableCollection<string> SavedPresets { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private int _currentIndex = -1;

    [ObservableProperty]
    private string _statusSummary = "Fila parada";

    [ObservableProperty]
    private bool _autoLogoutWhenFinished;

    [ObservableProperty]
    private bool _autoCloseWhenFinished;

    public event Action<bool>? StateChanged;

    public ScriptQueueService(IScriptInterface bot, ILogService logger)
    {
        _bot = bot;
        _logger = logger;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _presetsDirectory = Path.Combine(home, ".config", "Skua", "Queues");
        EnsurePresetsDirectory();
        RefreshPresets();

        StrongReferenceMessenger.Default.Register<ScriptQueueService, ScriptStoppedMessage, int>(
            this, (int)MessageChannels.ScriptStatus, (r, _) => r.OnScriptStopped());
    }

    private void EnsurePresetsDirectory()
    {
        try
        {
            if (!Directory.Exists(_presetsDirectory))
                Directory.CreateDirectory(_presetsDirectory);
        }
        catch { }
    }

    public void RefreshPresets()
    {
        try
        {
            SavedPresets.Clear();
            if (Directory.Exists(_presetsDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(_presetsDirectory, "*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrWhiteSpace(name))
                        SavedPresets.Add(name);
                }
            }
        }
        catch { }
    }

    private void OnScriptStopped()
    {
        if (IsRunning && _currentScriptTcs is not null)
        {
            _currentScriptTcs.TrySetResult(true);
        }
    }

    public void AddItem(string scriptPath, string className = "", bool smartEnhance = false, int delay = 3)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            return;

        string trimmedPath = scriptPath.Trim('"', '\'');
        string name = Path.GetFileName(trimmedPath);

        Items.Add(new ScriptQueueItem
        {
            ScriptPath = trimmedPath,
            DisplayName = name,
            ClassName = className.Trim(),
            EnableSmartEnhance = smartEnhance,
            DelaySeconds = Math.Max(0, delay),
            Status = QueueItemStatus.Pending,
            StatusMessage = "Pendente"
        });

        UpdateSummary();
    }

    public void RemoveItem(ScriptQueueItem item)
    {
        Items.Remove(item);
        UpdateSummary();
    }

    public void MoveUp(ScriptQueueItem item)
    {
        int index = Items.IndexOf(item);
        if (index > 0)
        {
            Items.Move(index, index - 1);
            UpdateSummary();
        }
    }

    public void MoveDown(ScriptQueueItem item)
    {
        int index = Items.IndexOf(item);
        if (index >= 0 && index < Items.Count - 1)
        {
            Items.Move(index, index + 1);
            UpdateSummary();
        }
    }

    public void Clear()
    {
        if (IsRunning)
            StopQueue();

        Items.Clear();
        CurrentIndex = -1;
        UpdateSummary();
    }

    public void StartQueue()
    {
        if (IsRunning || Items.Count == 0)
            return;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        StateChanged?.Invoke(true);
        _logger.ScriptLog($"[Queue] Iniciando fila de execução com {Items.Count} itens...");

        _ = Task.Run(() => RunQueueLoopAsync(_cts.Token));
    }

    public void StopQueue()
    {
        if (!IsRunning)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _currentScriptTcs?.TrySetCanceled();

        try
        {
            _ = _bot.Manager.StopScript();
        }
        catch { }

        IsRunning = false;
        CurrentIndex = -1;
        StatusSummary = "Fila interrompida pelo usuário";
        StateChanged?.Invoke(false);
        _logger.ScriptLog("[Queue] Fila interrompida.");
    }

    public void SkipCurrent()
    {
        if (!IsRunning || CurrentIndex < 0 || CurrentIndex >= Items.Count)
            return;

        ScriptQueueItem current = Items[CurrentIndex];
        current.Status = QueueItemStatus.Skipped;
        current.StatusMessage = "Pulado";
        _logger.ScriptLog($"[Queue] Pulando item [{CurrentIndex + 1}] '{current.DisplayName}'...");

        try
        {
            _ = _bot.Manager.StopScript();
        }
        catch { }
    }

    private async Task RunQueueLoopAsync(CancellationToken token)
    {
        try
        {
            // Reset items that were pending or failed
            for (int k = 0; k < Items.Count; k++)
            {
                if (Items[k].Status != QueueItemStatus.Completed)
                {
                    Items[k].Status = QueueItemStatus.Pending;
                    Items[k].StatusMessage = "Pendente";
                }
            }

            for (int i = 0; i < Items.Count; i++)
            {
                if (token.IsCancellationRequested)
                    break;

                CurrentIndex = i;
                ScriptQueueItem item = Items[i];

                if (item.Status == QueueItemStatus.Completed)
                    continue;

                item.Status = QueueItemStatus.Running;
                item.StatusMessage = "Aguardando conexão ao jogo...";
                StatusSummary = $"Executando [{i + 1}/{Items.Count}]: {item.DisplayName}";
                _logger.ScriptLog($"[Queue] [{i + 1}/{Items.Count}] Preparando: '{item.DisplayName}'...");

                // Wait for player to be in-game and world loaded
                while ((!_bot.Player.Playing || !_bot.Flash.IsWorldLoaded) && !token.IsCancellationRequested)
                {
                    await Task.Delay(500, token);
                }

                if (token.IsCancellationRequested)
                    break;

                // 1. Equip class if specified
                if (!string.IsNullOrWhiteSpace(item.ClassName))
                {
                    item.StatusMessage = $"Equipando classe '{item.ClassName}'...";
                    _logger.ScriptLog($"[Queue] Equipando classe: {item.ClassName}");

                    bool hasClass = _bot.Inventory.Contains(item.ClassName);
                    if (!hasClass && _bot.Bank.Contains(item.ClassName))
                    {
                        _bot.Bank.ToInventory(item.ClassName);
                        await Task.Delay(500, token);
                        hasClass = _bot.Inventory.Contains(item.ClassName);
                    }

                    if (hasClass)
                    {
                        _bot.Inventory.EquipItem(item.ClassName);
                        for (int w = 0; w < 10 && !_bot.Inventory.IsEquipped(item.ClassName); w++)
                        {
                            await Task.Delay(500, token);
                        }
                    }
                    else
                    {
                        _logger.ScriptLog($"[Queue] Aviso: Classe '{item.ClassName}' não encontrada no inventário nem no banco.");
                    }
                }

                // 2. Run Smart Enhance if enabled
                if (item.EnableSmartEnhance)
                {
                    item.StatusMessage = "Executando Smart Enhance...";
                    _logger.ScriptLog("[Queue] Aplicando Smart Enhance antes de iniciar o script...");
                    await RunSmartEnhanceAsync(token);
                }

                // 3. Optional cooldown delay
                if (item.DelaySeconds > 0)
                {
                    item.StatusMessage = $"Aguardando {item.DelaySeconds}s de cooldown...";
                    await Task.Delay(item.DelaySeconds * 1000, token);
                }

                // 4. Resolve and start script
                string resolvedPath = ResolveScriptPath(item.ScriptPath);
                if (!File.Exists(resolvedPath))
                {
                    item.Status = QueueItemStatus.Failed;
                    item.StatusMessage = "Script não encontrado!";
                    _logger.ScriptLog($"[Queue] Erro: Script '{item.ScriptPath}' não encontrado em nenhum caminho.");
                    continue;
                }

                item.StatusMessage = "Executando...";
                _logger.ScriptLog($"[Queue] [{i + 1}/{Items.Count}] Iniciando script: '{item.DisplayName}'...");

                TaskCompletionSource<bool> scriptDoneTcs = new();
                _currentScriptTcs = scriptDoneTcs;

                StrongReferenceMessenger.Default.Send<StartScriptMessage, int>(
                    new(resolvedPath), (int)MessageChannels.ScriptStatus);

                using (token.Register(() => scriptDoneTcs.TrySetCanceled()))
                {
                    try
                    {
                        await scriptDoneTcs.Task;
                        if (item.Status != QueueItemStatus.Skipped)
                        {
                            item.Status = QueueItemStatus.Completed;
                            item.StatusMessage = "Concluído";
                            _logger.ScriptLog($"[Queue] [{i + 1}/{Items.Count}] Script '{item.DisplayName}' finalizado com sucesso!");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (item.Status != QueueItemStatus.Skipped)
                        {
                            item.Status = QueueItemStatus.Failed;
                            item.StatusMessage = "Interrompido";
                        }
                        break;
                    }
                }

                _currentScriptTcs = null;

                // Safety delay between scripts
                await Task.Delay(2500, token);
            }

            StatusSummary = "Fila de scripts concluída com sucesso!";
            _logger.ScriptLog("[Queue] Todos os itens da fila foram concluídos!");

            if (AutoLogoutWhenFinished)
            {
                _logger.ScriptLog("[Queue] Deslogando da conta (AutoLogout ativado)...");
                _bot.Servers.Logout();
            }

            if (AutoCloseWhenFinished)
            {
                _logger.ScriptLog("[Queue] Encerrando o cliente Skua em 3s (AutoClose ativado)...");
                await Task.Delay(3000, token);
                Environment.Exit(0);
            }
        }
        catch (OperationCanceledException)
        {
            StatusSummary = "Fila cancelada";
            _logger.ScriptLog("[Queue] Execução da fila cancelada.");
        }
        catch (Exception ex)
        {
            StatusSummary = $"Erro na fila: {ex.Message}";
            _logger.ScriptLog($"[Queue Erro] {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            CurrentIndex = -1;
            StateChanged?.Invoke(false);
        }
    }

    private async Task RunSmartEnhanceAsync(CancellationToken token)
    {
        try
        {
            string smartEnhanceScript = Path.Combine(ClientFileSources.SkuaScriptsDIR, "Enhancement", "SmartEnhance.cs");
            if (!File.Exists(smartEnhanceScript))
            {
                _logger.ScriptLog("[Queue] SmartEnhance.cs não encontrado em Scripts/Enhancement/. Pulando pré-enhancement.");
                return;
            }

            TaskCompletionSource<bool> enhanceDone = new();
            _currentScriptTcs = enhanceDone;

            StrongReferenceMessenger.Default.Send<StartScriptMessage, int>(
                new(smartEnhanceScript), (int)MessageChannels.ScriptStatus);

            using (token.Register(() => enhanceDone.TrySetCanceled()))
            {
                await enhanceDone.Task;
            }
            _currentScriptTcs = null;
            _logger.ScriptLog("[Queue] Smart Enhance aplicado com sucesso.");
            await Task.Delay(1500, token);
        }
        catch (Exception ex)
        {
            _logger.ScriptLog($"[Queue] Falha no Smart Enhance: {ex.Message}. Prosseguindo...");
        }
    }

    private string ResolveScriptPath(string rawPath)
    {
        if (File.Exists(rawPath))
            return rawPath;

        string inScripts = Path.Combine(ClientFileSources.SkuaScriptsDIR, rawPath);
        if (File.Exists(inScripts))
            return inScripts;

        return rawPath;
    }

    private void UpdateSummary()
    {
        if (IsRunning)
            return;

        StatusSummary = Items.Count == 0 ? "Fila vazia" : $"{Items.Count} script(s) na fila";
    }

    public bool SavePreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            EnsurePresetsDirectory();
            string file = Path.Combine(_presetsDirectory, $"{name.Trim()}.json");

            ScriptQueuePreset preset = new()
            {
                Name = name.Trim(),
                AutoLogoutWhenFinished = AutoLogoutWhenFinished,
                AutoCloseWhenFinished = AutoCloseWhenFinished,
                Items = Items.Select(i => new ScriptQueueItem
                {
                    ScriptPath = i.ScriptPath,
                    DisplayName = i.DisplayName,
                    ClassName = i.ClassName,
                    EnableSmartEnhance = i.EnableSmartEnhance,
                    DelaySeconds = i.DelaySeconds
                }).ToList()
            };

            string json = JsonConvert.SerializeObject(preset, Formatting.Indented);
            File.WriteAllText(file, json);

            RefreshPresets();
            _logger.ScriptLog($"[Queue] Fila salva com sucesso: '{name}' ({preset.Items.Count} itens)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.ScriptLog($"[Queue] Erro ao salvar fila '{name}': {ex.Message}");
            return false;
        }
    }

    public bool LoadPreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            EnsurePresetsDirectory();
            string file = Path.Combine(_presetsDirectory, $"{name.Trim()}.json");
            if (!File.Exists(file))
                file = Path.Combine(_presetsDirectory, name.Trim());

            if (!File.Exists(file))
            {
                _logger.ScriptLog($"[Queue] Arquivo da fila '{name}' não encontrado.");
                return false;
            }

            string json = File.ReadAllText(file);
            ScriptQueuePreset? preset = JsonConvert.DeserializeObject<ScriptQueuePreset>(json);
            if (preset is null)
                return false;

            if (IsRunning)
                StopQueue();

            Items.Clear();
            AutoLogoutWhenFinished = preset.AutoLogoutWhenFinished;
            AutoCloseWhenFinished = preset.AutoCloseWhenFinished;

            foreach (ScriptQueueItem item in preset.Items)
            {
                Items.Add(new ScriptQueueItem
                {
                    ScriptPath = item.ScriptPath,
                    DisplayName = item.DisplayName,
                    ClassName = item.ClassName,
                    EnableSmartEnhance = item.EnableSmartEnhance,
                    DelaySeconds = item.DelaySeconds,
                    Status = QueueItemStatus.Pending,
                    StatusMessage = "Pendente"
                });
            }

            UpdateSummary();
            _logger.ScriptLog($"[Queue] Fila '{name}' carregada com sucesso ({Items.Count} itens).");
            return true;
        }
        catch (Exception ex)
        {
            _logger.ScriptLog($"[Queue] Erro ao carregar fila '{name}': {ex.Message}");
            return false;
        }
    }

    public bool DeletePreset(string name)
    {
        try
        {
            string file = Path.Combine(_presetsDirectory, $"{name.Trim()}.json");
            if (File.Exists(file))
            {
                File.Delete(file);
                RefreshPresets();
                _logger.ScriptLog($"[Queue] Fila '{name}' excluída com sucesso.");
                return true;
            }
        }
        catch { }
        return false;
    }

    public void Dispose()
    {
        StopQueue();
    }
}
