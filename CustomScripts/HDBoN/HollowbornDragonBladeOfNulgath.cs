/*
name: Hollowborn DragonBlade of Nulgath (Bengt)
description: Ordered solo progression for Lae's Fiend's Shadow and Fiend's Purgatory quests.
tags: hollowborn, nulgath, dragonblade, hdbon
*/
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/Hollowborn/CoreHollowborn.cs
//cs_include Scripts/Nation/CoreNation.cs

using Skua.Core.Interfaces;

public class HollowbornDragonBladeOfNulgath
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private CoreAdvanced Adv => CoreAdvanced.Instance;
    private string? _lastEnhancedClass;

    private static CoreHollowborn Hollowborn
    {
        get => _hollowborn ??= new CoreHollowborn();
        set => _hollowborn = value;
    }
    private static CoreHollowborn _hollowborn;

    private static CoreNation Nation
    {
        get => _nation ??= new CoreNation();
        set => _nation = value;
    }
    private static CoreNation _nation;

    private const int HardcoreContract = 55157;
    private const int InTheFiendsShadow = 10789;
    private const int FiendsPurgatory = 10790;
    private const int PhoenixBlade = 300;
    private const int ChaoticTentacles = 16877;
    private const int TotemOfNulgath = 5357;
    private const int HollowSoul = 55159;
    private const int DragonFire = 1316;
    private const int CrimsonPlate = 4695;
    private const int CrimsonFacePlate = 4961;
    private const int UngodlyReavers = 4939;
    private const int CrystalPhoenixBlade = 6137;
    private const int OverfiendBlade = 6138;
    private const int DarkMakai = 6142;
    private const int HollowbornDragonBlade = 101904;

    public void ScriptMain(IScriptInterface bot)
    {
        // EquipClass raises this event after each successful class equip. Keep the
        // listener scoped to this runner and avoid re-enhancing on repeated calls
        // that leave the same class equipped.
        CoreBots.OnEquipClass += EnhanceEquippedClass;

        Core.SetOptions();
        Core.Logger("HDBON_RUNNER_V1: Bengt progression started.");

        if (Bot.Player.Level < 85)
        {
            Core.Logger($"HDBON requires level 85; Bengt is level {Bot.Player.Level}.", messageBox: true, stopBot: true);
            return;
        }

        if (!Bot.Player.IsMember)
        {
            Core.Logger("HDBON is paused: the required DragonFire of Nulgath is Legend-tagged, and this account is not currently Member.", messageBox: true, stopBot: true);
            return;
        }

        Core.Logger("HDBON_RUNNER_V1: Bengt validated; proceeding in dependency order.");
        Core.SoloClass = "Verus DoomKnight";
        Core.EquipClass(ClassType.Solo);
        Core.AddDrop(
            "Lae's Hardcore Contract",
            "Phoenix Blade",
            "Chaotic Tentacles",
            "Hollow Soul",
            "Blighted Zubami Badge",
            "Spoil of Salek",
            "Sarah's Souvenir",
            "Hollowborn DragonBlade of Nulgath"
        );

        // The contract and most of Bengt's Hollow Souls are already banked.
        Hollowborn.HardcoreContract();
        Core.CheckInventory(HardcoreContract);
        if (Bot.ShouldExit)
            return;

        if (!Core.CheckInventory(HollowbornDragonBlade))
        {
            CompleteFiendsShadow();
            if (Bot.ShouldExit)
                return;

            PreparePurgatoryItems();
            if (Bot.ShouldExit)
                return;

            CompleteFiendsPurgatory();
            if (Bot.ShouldExit)
                return;
        }

        if (Core.CheckInventory(HollowbornDragonBlade))
            Core.Logger("HDBON_RUNNER_V1: Hollowborn DragonBlade of Nulgath obtained.");

        Core.SetOptions(false);
    }

    private void EnhanceEquippedClass(string className)
    {
        if (string.IsNullOrWhiteSpace(className)
            || string.Equals(_lastEnhancedClass, className, StringComparison.OrdinalIgnoreCase))
            return;

        _lastEnhancedClass = className;
        Core.Logger($"HDBON_RUNNER_V1: SmartEnhance for {className}.");
        Adv.SmartEnhance(className);
    }

    private void CompleteFiendsShadow()
    {
        if (!Core.isCompletedBefore(InTheFiendsShadow, log: false))
        {
            Core.Logger("HDBON stage 1/3: completing In the Fiend's Shadow.");
            Core.EnsureAccept(InTheFiendsShadow);

            if (!Core.CheckInventory(PhoenixBlade))
                Core.HuntMonster("lair", "Onyx Lava Dragon", "Phoenix Blade", isTemp: false);

            if (!Core.CheckInventory(ChaoticTentacles))
                Core.HuntMonster("stormtemple", "Chaos Lord Lionfang", "Chaotic Tentacles", isTemp: false);

            if (!Core.CheckInventory(TotemOfNulgath))
                Nation.FarmTotemofNulgath(1);

            if (!Core.CheckInventory(HollowSoul, 50))
            {
                Core.Logger("The required 50 Hollow Souls are unavailable. Stopping instead of farming the similarly named Human Soul.", messageBox: true, stopBot: true);
                return;
            }

            if (!Core.EnsureComplete(InTheFiendsShadow, 101906))
            {
                Core.Logger("In the Fiend's Shadow is not ready to turn in. Check its required items and quest state.", messageBox: true, stopBot: true);
                return;
            }
        }
        else
            Core.Logger("HDBON stage 1/3: In the Fiend's Shadow already complete.");
    }

    private void PreparePurgatoryItems()
    {
        Core.Logger("HDBON stage 2/3: preparing the seven Nulgath items.");

        GetTwistedItem(DragonFire);
        GetTwistedItem(CrimsonPlate);
        GetTwistedItem(CrimsonFacePlate);

        GetJuggernautItem(UngodlyReavers);
        GetJuggernautItem(CrystalPhoenixBlade);
        GetJuggernautItem(OverfiendBlade);
        GetJuggernautItem(DarkMakai);

        int[] required =
        {
            DragonFire,
            CrimsonPlate,
            CrimsonFacePlate,
            UngodlyReavers,
            CrystalPhoenixBlade,
            OverfiendBlade,
            DarkMakai
        };

        foreach (int item in required)
        {
            if (!Core.CheckInventory(item))
            {
                Core.Logger($"HDBON preparation stopped: required item ID {item} is still missing.", messageBox: true, stopBot: true);
                return;
            }
        }
    }

    private void GetTwistedItem(int itemId)
    {
        if (Core.CheckInventory(itemId))
            return;

        Core.Logger($"HDBON preparation: farming Twisted Items reward {itemId}.");
        Core.EnsureAccept(765);
        Nation.FarmTotemofNulgath(3);
        Core.HuntMonster("underworld", "Skull Warrior", "Skull Warrior Rune");

        if (!Core.EnsureComplete(765, itemId) || !Core.CheckInventory(itemId))
            Core.Logger($"Twisted Items did not produce item ID {itemId}; stopping for review.", messageBox: true, stopBot: true);
    }

    private void GetJuggernautItem(int itemId)
    {
        if (Core.CheckInventory(itemId, toInv: false))
        {
            Core.CheckInventory(itemId); // Move a banked requirement into Bengt's inventory.
            return;
        }

        Core.Logger($"HDBON preparation: farming Juggernaut reward {itemId}.");
        Core.EnsureAccept(837);
        Nation.FarmUni13(1);
        Nation.FarmDiamondofNulgath(13);
        Nation.FarmDarkCrystalShard(50);
        Nation.FarmTotemofNulgath(3);
        Nation.FarmGemofNulgath(20);
        Nation.FarmVoucher(false);
        Nation.FarmTaintedGem(50);
        Core.HuntMonster("underworld", "Undead Bruiser", "Undead Bruiser Rune");

        if (!Core.EnsureComplete(837, itemId) || !Core.CheckInventory(itemId))
            Core.Logger($"Juggernaut Items did not produce item ID {itemId}; stopping for review.", messageBox: true, stopBot: true);
    }

    private void CompleteFiendsPurgatory()
    {
        Core.Logger("HDBON stage 3/3: starting Fiend's Purgatory and its three boss drops.");
        Core.EnsureAccept(FiendsPurgatory);

        Core.HuntMonster("deleuzetundra", "Blighted Zubami", "Blighted Zubami Badge");
        Core.HuntMonster("voidsalek", "Salek Sprayer", "Spoil of Salek");
        Core.HuntMonster("voidnerfkitten", "Sarah the NerfKitten", "Sarah's Souvenir");

        if (!Core.EnsureComplete(FiendsPurgatory, HollowbornDragonBlade))
            Core.Logger("Fiend's Purgatory could not be turned in for the HDBoN. Check quest drops and inventory.", messageBox: true, stopBot: true);
    }
}
