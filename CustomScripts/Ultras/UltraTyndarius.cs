/*
name: UltraTyndarius
description: Coordinated 4-account Ultra Avatar Tyndarius daily runner using Ultrasv3.
tags: ultra, tyndarius, dauntless, daily
*/
//cs_include Scripts/Ultrasv3/DependenciesUltras/CoreEnginev3.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/CoreUltrav3.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraEnhancements.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraPotions.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraGeneral.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraCustomClassSync.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraWaitForArmy.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/GetScrolls.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/CoreAdvanced.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Skua.Core.Interfaces;

public class UltraTyndarius
{
    private static IScriptInterface Bot => IScriptInterface.Instance;
    private static CoreBots C => CoreBots.Instance;
    private static CoreEnginev3 Engine => CoreEnginev3.Instance;
    private static CoreUltrav3 Ultra => _Ultra ??= new CoreUltrav3();
    private static CoreUltrav3? _Ultra;
    private static UltraEnhancements Enh => _Enh ??= new UltraEnhancements();
    private static UltraEnhancements? _Enh;
    private static UltraPotions Pots => _Pots ??= new UltraPotions();
    private static UltraPotions? _Pots;
    private static GetScrolls Scrolls => _Scrolls ??= new GetScrolls();
    private static GetScrolls? _Scrolls;

    private const int ArmySize = 4;
    private const string MapName = "ultratyndarius";
    private const string BossName = "Ultra Avatar Tyndarius";
    private const string OrbName = "Ultra Fire Orb";
    private const string BossDefeatedTemp = "Ultra Avatar Tyndarius Defeated";
    private const int DailyQuestId = 8245;
    private const string BossFocusSyncFile = "ultra_tyndarius_bossfocus.sync";

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { "StoneCrusher", "Verus DoomKnight", "Dragon of Time" },
        new[] { "ArchPaladin" },
        new[] { "Legion Revenant" },
        new[] { "Lord of Order" }
    };

    private static readonly Dictionary<string, string> PreferredAssignments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bengt of worlds"] = "Legion Revenant",
        ["Little_mett"] = "ArchPaladin",
        ["MrPresident BR"] = "StoneCrusher",
        ["LeesDragon Prime"] = "Lord of Order"
    };

    private DateTime _nextBossRejoinAttemptUtc = DateTime.MinValue;
    private DateTime _nextTauntAttemptUtc = DateTime.MinValue;
    private DateTime _focusMissingSinceUtc = DateTime.MinValue;
    private DateTime _bossLeaseExpiredSinceUtc = DateTime.MinValue;
    private string _bossCell = "Boss";

    public void ScriptMain(IScriptInterface bot)
    {
        RunBoss();
        Bot.StopSync();
    }

    public void RunBoss()
    {
        C.SetOptions(true);
        Engine.Boot();

        try
        {
            Prep();
            Fight();
        }
        finally
        {
            Bot.Skills.Stop();
            Engine.DisableSkills();
            C.SetOptions(false);
        }
    }

    private void Prep()
    {
        UltraGeneral.EquipWarriorClass();
        Bot.Sleep(1500);

        C.Logger($"[UltraTyndarius] Equipping role-based classes for army size {ArmySize}.");
        UltraCustomClassSync.CustomClassSync(
            Ultra,
            Bot,
            UltraClassesByRole,
            ArmySize,
            "ultra_tyndarius_class.sync",
            allowDuplicates: false,
            preferredUsernameAssignments: PreferredAssignments
        );
        Bot.Sleep(1500);

        Enh.ApplyTyndarius();
        ApplySurvivalEnhancements();
        C.Logger($"[UltraTyndarius] Role class ready: {Bot.Player.CurrentClass?.Name ?? "Unknown"}");
    }

    private void ApplySurvivalEnhancements()
    {
        var adv = CoreAdvanced.Instance;
        string cls = Bot.Player.CurrentClass?.Name ?? string.Empty;
        bool hasPenitence = C.isCompletedBefore(8822, log: false);

        if (string.Equals(cls, "Legion Revenant", StringComparison.OrdinalIgnoreCase))
        {
            // Use Ravenous if unlocked for huge Orb burst + Consumed (-20% enemy damage/crit), keeping Wizard+Penitence (~4k HP).
            WeaponSpecial w = C.isCompletedBefore(9560, log: false)
                ? WeaponSpecial.Ravenous
                : (C.isCompletedBefore(8741, log: false) ? WeaponSpecial.Valiance : WeaponSpecial.Awe_Blast);
            adv.EnhanceEquipped(
                type: EnhancementType.Wizard,
                cSpecial: hasPenitence ? CapeSpecial.Penitence : CapeSpecial.None,
                hSpecial: HelmSpecial.None,
                wSpecial: w
            );
        }
        else if (string.Equals(cls, "ArchPaladin", StringComparison.OrdinalIgnoreCase))
        {
            adv.EnhanceEquipped(
                type: EnhancementType.Fighter,
                cSpecial: hasPenitence ? CapeSpecial.Penitence : CapeSpecial.None,
                hSpecial: HelmSpecial.None,
                wSpecial: C.isCompletedBefore(8739, log: false) ? WeaponSpecial.Lacerate : WeaponSpecial.Awe_Blast
            );
        }
        else if (string.Equals(cls, "Verus DoomKnight", StringComparison.OrdinalIgnoreCase))
        {
            WeaponSpecial w = C.isCompletedBefore(9560, log: false) ? WeaponSpecial.Ravenous
                : (C.isCompletedBefore(8741, log: false) ? WeaponSpecial.Valiance : WeaponSpecial.Lacerate);
            adv.EnhanceEquipped(
                type: EnhancementType.Fighter,
                cSpecial: hasPenitence ? CapeSpecial.Penitence : CapeSpecial.None,
                hSpecial: C.isCompletedBefore(8828, log: false) ? HelmSpecial.Forge : HelmSpecial.None,
                wSpecial: w
            );
        }
    }

    private void Fight()
    {
        const string waitSyncPrep = "ultra_tyndarius_prep.sync";
        const string waitSyncBoss = "ultra_tyndarius_boss.sync";
        const string completionSyncFile = "UltraTyndariusCompletion.sync";

        Bot.Options.DisableCollisions = true;
        Bot.Drops.Add("Avatar Tyndarius Insignia");
        Bot.Drops.Add("Body Tonic");

        // Unlock /ultratyndarius and Galanoth quest 8245 on alts without FireAvatar story progress.
        if (Bot.Flash.CallGameFunction<int>("world.getQuestValue", 412) < 22)
            Bot.Quests.UpdateQuest(22, 412);

        bool dailyAlreadyComplete = Bot.Quests.IsDailyComplete(DailyQuestId);
        if (dailyAlreadyComplete)
            C.Logger("[UltraTyndarius] Daily quest 8245 already completed on this account today; assisting party.");
        else if (!UltraGeneral.IsQuestGreen(Bot, DailyQuestId))
            UltraGeneral.EnsureAcceptOnce(Bot, DailyQuestId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(completionSyncFile));
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(BossFocusSyncFile));

        string currentClass = Bot.Player.CurrentClass?.Name ?? string.Empty;
        bool isMainTaunter = string.Equals(currentClass, "ArchPaladin", StringComparison.OrdinalIgnoreCase);
        bool isOrbTaunter = string.Equals(currentClass, "Legion Revenant", StringComparison.OrdinalIgnoreCase);
        bool isBackupTaunter = string.Equals(currentClass, "StoneCrusher", StringComparison.OrdinalIgnoreCase);
        bool isTaunter = isMainTaunter || isOrbTaunter || isBackupTaunter;

        // Keep Body Tonic on LR too (3090 -> 4040 HP) while still swapping Elixir to Malevolence below.
        string potContext = isTaunter ? "Speaker" : string.Empty;
        if (!Bot.Inventory.Contains("Body Tonic", 1)
            && C.CheckInventory(new[] { "Roc Tongue", "Chaoroot", "Dragon Runestone" })
            && Bot.Inventory.TryGetItem("Roc Tongue", out var roc) && roc != null
            && Bot.Inventory.TryGetItem("Chaoroot", out var chao) && chao != null)
        {
            C.Logger("[UltraTyndarius] Crafting Body Tonic directly from stocked reagents.");
            C.Join("alchemy");
            for (int c = 0; c < 3 && !Bot.ShouldExit && !Bot.Inventory.Contains("Body Tonic", 3); c++)
            {
                if (!C.CheckInventory(new[] { "Roc Tongue", "Chaoroot", "Dragon Runestone" }))
                    break;
                C.SendPackets($"%xt%zm%crafting%1%alchStart%{roc.ID}%{chao.ID}%{AlchemyRunes.Gebo}%1%{CoreFarms.AlchemyTraits.End}%");
                Bot.Sleep(16500);
                C.SendPackets("%xt%zm%crafting%1%alchDone%");
                Bot.Sleep(1500);
                Bot.Drops.Pickup("Body Tonic");
                Bot.Sleep(400);
            }
        }

        if (Bot.Player.Gold >= 5_500_000)
            Pots.EnsureRecommendedPotions(desiredQuant: 1, skipThird: true, context: potContext);
        else
            C.Logger($"[UltraTyndarius] Skipping potion crafting (gold={Bot.Player.Gold:N0}); using stocked consumables.");

        if (isTaunter)
            Scrolls.GetScrollOfEnrage();

        C.Join("Whitemap");
        WaitForPartySync(waitSyncPrep, useSkill: false);

        Pots.UseRecommendedPotions(desiredQuant: 1, skipThird: true, context: potContext, ensureStock: false);

        if (!Bot.Inventory.Contains("Body Tonic", 1))
            UsePotionIfOwned("Fate Tonic");

        if (isOrbTaunter)
            UsePotionIfOwned("Potent Malevolence Elixir");

        if (isTaunter)
        {
            C.Logger($"[UltraTyndarius] {currentClass} equipping Scroll of Enrage.");
            Engine.EquipEnrage();
        }
        else if (Bot.Inventory.Contains("Potent Malice Potion") && Bot.Inventory.GetQuantity("Potent Malice Potion") > 0)
        {
            Engine.EquipConsumable("Potent Malice Potion");
        }
        else if (Bot.Inventory.Contains("Felicitous Philtre") && Bot.Inventory.GetQuantity("Felicitous Philtre") > 0)
        {
            Engine.EquipConsumable("Felicitous Philtre");
        }

        Engine.Join(MapName);
        Bot.Sleep(2500);
        WaitForPartySync(waitSyncBoss, useSkill: true);

        if (isMainTaunter)
        {
            // Managed per-tick in Fight() so AP prioritizes Hymn of Light below 70% HP without skill 4 wiping stacks.
            Engine.DisableSkills();
            C.Logger("[UltraTyndarius] ArchPaladin using priority 2(<70% HP) | 3 | 2 | 1 loop.");
        }

        var (bestCell, _) = Engine.ChooseBestCell(BossName);
        if (!string.IsNullOrWhiteSpace(bestCell))
            _bossCell = bestCell!;
        Bot.Player.SetSpawnPoint();

        bool HasCompletedOrDropped() =>
            Bot.TempInv.Contains(BossDefeatedTemp, 1)
            || Bot.Quests.CanComplete(DailyQuestId)
            || Bot.Quests.IsDailyComplete(DailyQuestId);

        SyncAndCountReadyUsers(completionSyncFile, isReady: HasCompletedOrDropped());

        while (!Bot.ShouldExit)
        {
            if (Bot.Player?.Alive != true)
            {
                Bot.Wait.ForTrue(() => Bot.Player?.Alive == true, 20);
                Bot.Sleep(300);
            }

            if (!EnsureBossCell())
            {
                Bot.Sleep(1000);
                continue;
            }

            if (SyncAndCountReadyUsers(completionSyncFile, isReady: HasCompletedOrDropped()) >= ArmySize)
            {
                C.Logger("[UltraTyndarius] Ultra Avatar Tyndarius defeated by party. Finishing quest.");
                Engine.DisableSkills();
                Bot.Combat.CancelAutoAttack();
                Bot.Combat.CancelTarget();
                C.Jump("Enter", "Spawn");
                Bot.Player.SetSpawnPoint();
                Bot.Sleep(1500);
                Ultra.PersistentJoinHouse();
                if (!Bot.Quests.IsDailyComplete(DailyQuestId))
                {
                    UltraGeneral.CompleteQuest(Bot, DailyQuestId);
                    Bot.Sleep(1500);
                }
                C.Logger($"[UltraTyndarius] Avatar Tyndarius Insignia total: {Bot.Inventory.GetQuantity("Avatar Tyndarius Insignia")}");
                break;
            }

            if (isMainTaunter)
            {
                // ArchPaladin keeps Commandment + Lacerate + Focus on Boss and prioritizes Hymn heal if HP drops below 70%.
                Bot.Combat.Attack(BossName);
                TryTauntIfMissingFocus(0, bossOnly: true, publishBossLease: true, respectBossLease: true);
                CastArchPaladinPriority();
            }
            else if (isOrbTaunter)
            {
                // Legion Revenant clears Ultra Fire Orbs first while AoE chips Boss and taunts untaunted targets.
                Engine.KillWithPriority(BossName, OrbName);
                TryTauntIfMissingFocus(0);
            }
            else if (isBackupTaunter)
            {
                // StoneCrusher burns Orbs alongside LR + LoO and briefly swaps to Boss whenever Boss Focus lease expires.
                if (!TryBackupTauntBoss(150))
                    Engine.KillWithPriority(BossName, OrbName);
            }
            else
            {
                // Lord of Order focuses down Ultra Fire Orbs first, then Boss.
                Engine.KillWithPriority(BossName, OrbName);
                Pots.ActivateEquippedPotion();
            }

            Bot.Sleep(150);
        }
    }

    private int SyncAndCountReadyUsers(string syncFileName, bool isReady)
    {
        string syncPath = Ultra.ResolveSyncPath(syncFileName);
        string username = Bot.Player.Username ?? "Unknown";
        string myKey = username.Replace(":", "-");
        Ultra.UpdateEntry(syncPath, myKey, isReady ? "1" : "0");

        string[] lines = Ultra.ReadLines(syncPath);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in lines)
        {
            string[] parts = line.Split(':');
            if (parts.Length < 3 || parts[1] != "1")
                continue;
            if (!long.TryParse(parts[2], out long ts) || now - ts > 600)
                continue;

            string user = parts[0].Split('|')[0].Trim();
            if (!string.IsNullOrEmpty(user))
                users.Add(user);
        }

        return users.Count;
    }

    private void WaitForPartySync(string syncFileName, bool useSkill)
    {
        int lastReady = -1;

        while (!Bot.ShouldExit)
        {
            int ready = SyncAndCountReadyUsers(syncFileName, isReady: true);

            if (ready != lastReady)
            {
                lastReady = ready;
                C.Logger($"[UltraTyndarius] Party sync ({syncFileName}): {ready}/{ArmySize}");
            }

            if (ready >= ArmySize)
                break;

            Bot.Sleep(500);
        }

        if (Bot.ShouldExit)
            return;

        DateTime waitTill = DateTime.UtcNow.AddMilliseconds(2000);
        while (!Bot.ShouldExit && DateTime.UtcNow < waitTill)
        {
            if (useSkill)
            {
                Bot.Skills.UseSkill(3);
                Bot.Sleep(300);
                Bot.Skills.UseSkill(2);
                Bot.Sleep(300);
                Bot.Skills.UseSkill(1);
                Bot.Sleep(300);
            }
            else
            {
                Bot.Sleep(500);
            }
        }
    }

    private void UsePotionIfOwned(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Bot.Inventory.Contains(name, 1))
            return;

        Engine.EquipConsumable(name);
        Bot.Sleep(500);
        if (Bot.Inventory.IsEquipped(name))
        {
            Bot.Skills.UseSkill(5);
            Bot.Sleep(600);
        }
    }

    private void CastArchPaladinPriority()
    {
        if (!Bot.Player.Alive || !Bot.Player.HasTarget)
            return;

        if (Bot.Player.MaxHealth > 0 && Bot.Player.Health < Bot.Player.MaxHealth * 0.7 && Bot.Skills.CanUseSkill(2))
            Engine.Cast(2);
        else if (Bot.Skills.CanUseSkill(3))
            Engine.Cast(3);
        else if (Bot.Skills.CanUseSkill(2))
            Engine.Cast(2);
        else if (Bot.Skills.CanUseSkill(1))
            Engine.Cast(1);
    }

    private void TryTauntIfMissingFocus(int graceDelayMs, bool bossOnly = false, bool publishBossLease = false, bool respectBossLease = false)
    {
        if (!Bot.Player.Alive || !Bot.Player.HasTarget || DateTime.UtcNow < _nextTauntAttemptUtc)
            return;

        var target = Bot.Player.Target;
        if (target == null || target.HP <= 0)
            return;

        bool isBossTarget = string.Equals(target.Name, BossName, StringComparison.OrdinalIgnoreCase);
        if (bossOnly && !isBossTarget)
        {
            _focusMissingSinceUtc = DateTime.MinValue;
            return;
        }

        if (respectBossLease && isBossTarget && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < ReadBossFocusUntilMs())
        {
            _focusMissingSinceUtc = DateTime.MinValue;
            return;
        }

        bool hasFocus = Bot.Target?.Auras?.Any(a => a != null && string.Equals(a.Name, "Focus", StringComparison.OrdinalIgnoreCase)) == true;
        if (hasFocus)
        {
            _focusMissingSinceUtc = DateTime.MinValue;
            return;
        }

        if (_focusMissingSinceUtc == DateTime.MinValue)
            _focusMissingSinceUtc = DateTime.UtcNow;

        if (graceDelayMs > 0 && (DateTime.UtcNow - _focusMissingSinceUtc).TotalMilliseconds < graceDelayMs)
            return;

        if (Bot.Skills.CanUseSkill(5))
        {
            _nextTauntAttemptUtc = DateTime.UtcNow.AddMilliseconds(400);
            Engine.Cast(5);
            if (publishBossLease && isBossTarget)
                PublishBossFocusLease(5800, force: true);
        }
    }

    private bool TryBackupTauntBoss(int graceDelayMs)
    {
        if (!Bot.Player.Alive || DateTime.UtcNow < _nextTauntAttemptUtc || !Bot.Skills.CanUseSkill(5))
        {
            _bossLeaseExpiredSinceUtc = DateTime.MinValue;
            return false;
        }

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long untilMs = ReadBossFocusUntilMs();
        if (nowMs < untilMs)
        {
            _bossLeaseExpiredSinceUtc = DateTime.MinValue;
            return false;
        }

        if (_bossLeaseExpiredSinceUtc == DateTime.MinValue)
            _bossLeaseExpiredSinceUtc = DateTime.UtcNow;

        if ((DateTime.UtcNow - _bossLeaseExpiredSinceUtc).TotalMilliseconds < graceDelayMs)
            return false;

        if (!string.Equals(Bot.Player.Target?.Name, BossName, StringComparison.OrdinalIgnoreCase))
        {
            Bot.Combat.CancelTarget();
            Bot.Combat.Attack(BossName);
            for (int i = 0; i < 8 && !Bot.ShouldExit; i++)
            {
                if (string.Equals(Bot.Player.Target?.Name, BossName, StringComparison.OrdinalIgnoreCase))
                    break;
                Bot.Sleep(60);
            }
        }

        var boss = Bot.Player.Target;
        if (boss == null || boss.HP <= 0 || !string.Equals(boss.Name, BossName, StringComparison.OrdinalIgnoreCase))
            return false;

        bool hasFocus = Bot.Target?.Auras?.Any(a => a != null && string.Equals(a.Name, "Focus", StringComparison.OrdinalIgnoreCase)) == true;
        if (hasFocus)
        {
            _bossLeaseExpiredSinceUtc = DateTime.MinValue;
            PublishBossFocusLease(500);
            return false;
        }

        if (!Bot.Skills.CanUseSkill(5))
            return false;

        _nextTauntAttemptUtc = DateTime.UtcNow.AddMilliseconds(500);
        _bossLeaseExpiredSinceUtc = DateTime.MinValue;
        Engine.Cast(5);
        PublishBossFocusLease(5800, force: true);
        Bot.Sleep(150);
        return true;
    }

    private void PublishBossFocusLease(int durationMs, bool force = false)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long targetUntilMs = nowMs + durationMs;
        if (!force && targetUntilMs <= ReadBossFocusUntilMs() + 300)
            return;

        Ultra.UpdateEntry(Ultra.ResolveSyncPath(BossFocusSyncFile), "boss_focus_until", targetUntilMs.ToString());
    }

    private long ReadBossFocusUntilMs()
    {
        string[] lines = Ultra.ReadLines(Ultra.ResolveSyncPath(BossFocusSyncFile));
        foreach (string line in lines)
        {
            string[] parts = line.Split(':');
            if (parts.Length >= 2 && string.Equals(parts[0], "boss_focus_until", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(parts[1], out long untilMs))
            {
                return untilMs;
            }
        }
        return 0;
    }

    private bool EnsureBossCell()
    {
        string currentMap = Bot.Map?.Name?.Split('-')[0] ?? string.Empty;
        if (currentMap.Equals(MapName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Bot.Player?.Cell, _bossCell, StringComparison.OrdinalIgnoreCase))
            return true;

        if (DateTime.UtcNow < _nextBossRejoinAttemptUtc)
            return false;

        _nextBossRejoinAttemptUtc = DateTime.UtcNow.AddSeconds(10);
        C.Logger($"[UltraTyndarius] Outside boss cell (map={currentMap}, cell={Bot.Player?.Cell ?? "?"}); rejoining {MapName}/{_bossCell}.");
        Engine.Join(MapName, _bossCell, "Left");

        return Bot.Wait.ForTrue(
            () => (Bot.Map?.Name?.Split('-')[0] ?? string.Empty).Equals(MapName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Bot.Player?.Cell, _bossCell, StringComparison.OrdinalIgnoreCase),
            15
        );
    }
}
