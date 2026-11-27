//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraPotions.cs

using Skua.Core.Interfaces;

public class HDBoNConsumables
{
    private const string Philtre = "Felicitous Philtre";
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private readonly UltraPotions _potions = new();
    private DateTime _buffedAt = DateTime.MinValue;
    private DateTime _lastPhiltre = DateTime.MinValue;

    public void Stock()
    {
        if (!Core.CheckInventory(Philtre, 10))
            CoreAdvanced.Instance.BuyItem("alchemyacademy", 2036, Philtre, 10);
        _potions.EnsurePotions(desiredQuant: 3, skipThird: true);
    }

    public void Prepare()
    {
        if (DateTime.UtcNow - _buffedAt < TimeSpan.FromMinutes(8))
            return;
        _potions.UseRecommendedPotions(skipThird: true);
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
