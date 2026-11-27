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
//cs_include /home/max/Skua-Linux/CustomScripts/HDBoN/HDBoNConsumables.cs

using Skua.Core.Interfaces;
using System.IO;
using System.Linq;

public class HollowbornDragonBladeOfNulgath
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private CoreAdvanced Adv => CoreAdvanced.Instance;
    private readonly HDBoNConsumables _consumables = new();
    private string? _lastEnhancedClass;
    private int _bossDeaths;
    private const int MaxBossDeaths = 15;
    private const int BossTimeoutMinutes = 30;
    private (string Id, string Name) _serverRoom;
    private const string PotionPrepLockFile = "/home/max/.config/Skua/logs/hdbon_potion_prep.lock";

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
        _lastEnhancedClass = Bot.Player.CurrentClass?.Name;
        Core.EquipClass(ClassType.Solo);
        // Register keep-drops without unbanking every item up front. Required
        // quest items are unbanked on demand below, which avoids category-slot
        // checks failing before the inventory UI has finished initializing.
        Bot.Drops.Add(
            "Lae's Hardcore Contract",
            "Phoenix Blade",
            "Chaotic Tentacles",
            "Hollow Soul",
            "Blighted Zubami Badge",
            "Spoil of Salek",
            "Sarah's Souvenir",
            "Hollowborn DragonBlade of Nulgath",
            "Dual DragonBlades of Nulgath",
            "Fire Champion's Armor",
            "Nightmare Carnax's Guardians"
        );
        Core.Equip("Dual DragonBlades of Nulgath", "Fire Champion's Armor", "Nightmare Carnax's Guardians");

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
        Bot.Events.PlayerDeath += OnBossDeath;
        Bot.Events.ExtensionPacketReceived += ObserveServerRoom;
        Core.EnsureAccept(FiendsPurgatory);
        try
        {
            _consumables.ConfigureZubamiGear();
            using (AcquirePotionPrepLock())
            {
                if (Bot.ShouldExit)
                    return;
                _consumables.Stock();
            }
            _consumables.Prepare();

            if (!HuntBossWithParty("deleuzetundra", "Blighted Zubami", "Blighted Zubami Badge")
                || !HuntBossWithParty("voidsalek", "Salek Sprayer", "Spoil of Salek")
                || !HuntBossWithParty("voidnerfkitten", "Sarah the NerfKitten", "Sarah's Souvenir"))
                return;

            if (!Core.EnsureComplete(FiendsPurgatory, HollowbornDragonBlade))
                Core.Logger("Fiend's Purgatory could not be turned in for the HDBoN. Check quest drops and inventory.", messageBox: true, stopBot: true);
        }
        finally
        {
            Bot.Events.PlayerDeath -= OnBossDeath;
            Bot.Events.ExtensionPacketReceived -= ObserveServerRoom;
        }
    }

    private void ObserveServerRoom(dynamic packet)
    {
        try
        {
            var data = packet["params"]?.dataObj;
            if (packet["params"]?.type != "json" || data?.cmd != "moveToArea")
                return;
            _serverRoom = (Convert.ToString(data.areaId) ?? "", Convert.ToString(data.areaName) ?? "");
        }
        catch (Exception)
        {
            // Some extension packets are not map messages.
        }
    }

    private void OnBossDeath()
    {
        _bossDeaths++;
        Core.Logger($"HDBON_MONITOR: Bengt died during Fiend's Purgatory ({_bossDeaths} death(s) this run).", messageBox: false);
    }

    private FileStream? AcquirePotionPrepLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PotionPrepLockFile)!);
        while (!Bot.ShouldExit)
        {
            try
            {
                return new FileStream(PotionPrepLockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                Core.Sleep(500);
            }
        }
        return null;
    }

    private bool HuntBossWithParty(string map, string monster, string item)
    {
        bool HasDrop() => Bot.TempInv.Contains(item, 1) || Core.CheckInventory(item);

        if (HasDrop())
            return true;

        Core.Join(map);
        Bot.Wait.ForMapLoad(map);
        var target = Core.FindMonster(map, monster);
        if (target == null || string.IsNullOrEmpty(target.Cell))
            return false;

        Bot.Options.AggroAllMonsters = false;
        Bot.Options.AggroMonsters = false;
        Core.Jump(target.Cell, "Left");
        Bot.Wait.ForCellChange(target.Cell);

        _bossDeaths = 0;
        var deadline = DateTime.UtcNow.AddMinutes(BossTimeoutMinutes);
        var nextLog = DateTime.MinValue;
        string room = Bot.Map.RoomID.ToString();
        string fullRoom = _serverRoom.Id == room ? _serverRoom.Name : Bot.Map.FullName;
        if (fullRoom.StartsWith(map + "-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(fullRoom[(map.Length + 1)..], out _))
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(PotionPrepLockFile)!, "hdbon_leader_room.status"), $"{room}|{fullRoom}");
        string[] helpers = { "mrpresident_br", "leesdragon_prime", "the_borges_warrion", "little_mett", "bella_of_worlds" };

        while (!Bot.ShouldExit && DateTime.UtcNow < deadline && _bossDeaths < MaxBossDeaths)
        {
            if (HasDrop())
            {
                Core.Logger($"HDBON drop confirmed: {item}.");
                return true;
            }

            if (Bot.Player.Alive && Bot.Player.Cell != target.Cell)
                Core.Jump(target.Cell, "Left");

            if (Bot.Player.Alive && Bot.Player.Cell == target.Cell)
            {
                if (Bot.Player.HasTarget)
                    _consumables.UseInCombat();
                else
                {
                    var waiting = helpers.Where(account => !ButlerIsReady(account, map, room, target.Cell)).ToArray();
                    if (waiting.Length == 0)
                    {
                        if (Bot.Combat.Attack(monster) && DateTime.UtcNow >= nextLog)
                        {
                            Core.Logger($"HDBON party ready in {map}-{room}/{target.Cell}; attacking {monster}.");
                            nextLog = DateTime.UtcNow.AddSeconds(30);
                        }
                    }
                    else if (DateTime.UtcNow >= nextLog)
                    {
                        Core.Logger($"HDBON waiting for Butlers in {map}-{room}/{target.Cell}: {string.Join(", ", waiting)}.");
                        nextLog = DateTime.UtcNow.AddSeconds(30);
                    }
                }
            }
            Core.Sleep(500);
        }

        if (!Bot.ShouldExit)
            Core.Logger($"HDBON paused at {monster}: {_bossDeaths} leader deaths; drop not confirmed.", stopBot: true);
        return false;
    }

    private bool ButlerIsReady(string accountSlug, string map, string room, string cell)
    {
        string statusFile = Path.Combine("/home/max/.config/Skua/logs", $"hdbon_party_{accountSlug}.status");
        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(statusFile) > TimeSpan.FromSeconds(8))
                return false;
            string[] status = File.ReadAllText(statusFile).Split('|');
            return status.Length == 4 && status[0] == "READY"
                && string.Equals(status[1], map, StringComparison.OrdinalIgnoreCase)
                && status[2] == room
                && string.Equals(status[3], cell, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
