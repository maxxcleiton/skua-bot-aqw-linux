//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraPotions.cs

using Skua.Core.Interfaces;
using System.Linq;

public class HDBoNConsumables
{
    private const string Philtre = "Felicitous Philtre";
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private readonly UltraPotions _potions = new();
    private DateTime _buffedAt = DateTime.MinValue;
    private DateTime _lastPhiltre = DateTime.MinValue;

    private bool PhysicalDps => _potions.HasAssignedClass("Verus DoomKnight") || _potions.HasAssignedClass("Void Highlord");
    private bool ArchPaladin => _potions.HasAssignedClass("ArchPaladin");

    public void ConfigureZubamiGear()
    {
        var adv = CoreAdvanced.Instance;
        string className = Bot.Player.CurrentClass?.Name ?? "";
        var type = className is "Legion Revenant" or "StoneCrusher" or "Shaman" ? EnhancementType.Wizard : EnhancementType.Lucky;
        var cape = Bot.Inventory.Items.Find(i => i.Equipped && i.Category.ToString() == "Cape");
        if (cape != null)
        {
            var desired = Core.isCompletedBefore(8822, log: false) ? CapeSpecial.Penitence :
                (cape.EnhancementPatternID == (int)CapeSpecial.Vainglory && Core.isCompletedBefore(8743, log: false)
                    ? CapeSpecial.Absolution : CapeSpecial.None);
            if (desired != CapeSpecial.None || cape.EnhancementPatternID == (int)CapeSpecial.Vainglory)
                adv.EnhanceItem(cape.Name, type, cSpecial: desired, logging: true);
            var equipped = Bot.Inventory.Items.Find(i => i.Equipped && i.Category.ToString() == "Cape");
            Core.Logger($"HDBON gear: {className} cape {equipped?.Name}, pattern {equipped?.EnhancementPatternID} (29 Penitence, 24 Vainglory).");
        }

        const string blade = "Dual DragonBlades of Nulgath";
        if (_potions.HasAssignedClass("Verus DoomKnight") && Bot.Inventory.IsEquipped(blade) && Core.isCompletedBefore(9560, log: false))
        {
            adv.EnhanceItem(blade, EnhancementType.Lucky, wSpecial: WeaponSpecial.Ravenous, logging: true);
            Core.Logger($"HDBON gear: {blade}, proc {Bot.Inventory.Items.Find(i => i.Equipped && i.Name == blade)?.ProcID} (15 Ravenous).");
        }
    }

    public void Stock(bool craftClassPotions = true)
    {
        if (!Core.CheckInventory(Philtre, 10))
            CoreAdvanced.Instance.BuyItem("alchemyacademy", 2036, Philtre, 10);
        if (PhysicalDps)
        {
            BuyBuff("Fate Tonic", 7641);
            BuyBuff("Potent Battle Elixir", 8633);
        }
        else if (ArchPaladin)
        {
            BuyBuff("Potent Revitalize Elixir", 8642);
        }
        else if (craftClassPotions && _potions.GetRecommendedPotions().Take(2).Any(p => !Core.CheckInventory(p, 1)))
        {
            _potions.EnsurePotions(desiredQuant: 3, skipThird: true);
        }
    }

    private void BuyBuff(string name, int shopItemId)
    {
        if (!Core.CheckInventory(name, 1))
            CoreAdvanced.Instance.BuyItem("alchemyacademy", 2036, name, 1, shopItemID: shopItemId);
    }

    private void UseBuff(string name, string? fallback = null)
    {
        if (!Core.CheckInventory(name, 1))
            name = fallback ?? name;
        if (!Core.CheckInventory(name, 1))
        {
            Core.Logger($"HDBON buff unavailable: {name}.");
            return;
        }
        if (Bot.Self.Auras.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Core.Logger($"HDBON buff active: {name}.");
            return;
        }
        CoreEnginev3.Instance.EquipConsumable(name);
        if (Bot.Inventory.IsEquipped(name))
        {
            Core.UsePotion();
            Core.Logger($"HDBON buff used: {name}.");
        }
        else if (Bot.Self.Auras.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            Core.Logger($"HDBON buff active: {name}.");
        else
            Core.Logger($"HDBON buff failed to equip: {name}.");
    }

    public void Prepare()
    {
        if (DateTime.UtcNow - _buffedAt < TimeSpan.FromMinutes(8))
            return;
        string[] defaults = _potions.GetRecommendedPotions();
        if (PhysicalDps)
        {
            UseBuff("Fate Tonic", defaults[0]);
            UseBuff("Potent Battle Elixir", defaults[1]);
        }
        else if (ArchPaladin)
        {
            UseBuff("Potent Revitalize Elixir", "Revitalize Elixir");
        }
        else
        {
            foreach (string buff in defaults.Take(2))
                UseBuff(buff);
        }
        if (Bot.Inventory.GetQuantity(Philtre) > 0)
            CoreEnginev3.Instance.EquipConsumable(Philtre);
        Core.Logger($"HDBON: {Philtre} equipped: {Bot.Inventory.IsEquipped(Philtre)}.");
        _buffedAt = DateTime.UtcNow;
    }

    public void UseInCombat()
    {
        if (!Bot.Player.Alive || !Bot.Player.HasTarget || !Bot.Inventory.IsEquipped(Philtre)
            || DateTime.UtcNow - _lastPhiltre < TimeSpan.FromSeconds(60))
            return;
        _lastPhiltre = DateTime.UtcNow;
        Core.UsePotion();
    }
}
