/*
name: UltraSpeaker
description: Coordinated 4-account /ultraspeaker (Malgor, The First Speaker) weekly runner with packet-driven aura state, custom skill loops, Enrage-CD-aware taunt rotation, fast VDK Decay, and Equal zone rotation.
tags: ultra,ultraspeaker,malgor,dauntless,army
*/
//cs_include Scripts/Ultrasv3/DependenciesUltras/CoreEnginev3.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/CoreUltrav3.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraEnhancements.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraPotions.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraGeneral.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/UltraCustomClassSync.cs
//cs_include Scripts/Ultrasv3/DependenciesUltras/GetScrolls.cs
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreFarms.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

public class UltraSpeaker
{
    private static IScriptInterface Bot => IScriptInterface.Instance;
    private static CoreBots C => CoreBots.Instance;
    private static CoreEnginev3 Engine => CoreEnginev3.Instance;
    private static CoreUltrav3 Ultra => _Ultra ??= new CoreUltrav3();
    private static CoreUltrav3? _Ultra;
    private static UltraPotions Pots => _Pots ??= new UltraPotions();
    private static UltraPotions? _Pots;
    private static GetScrolls Scrolls => _Scrolls ??= new GetScrolls();
    private static GetScrolls? _Scrolls;

    private const int ArmySize = 4;
    private const int WeeklyQuestId = 9173;
    private const string BossMap = "ultraspeaker";
    private const string BossName = "The First Speaker";
    private const string BossDefeatedItem = "The First Speaker Silenced";

    // Role 0: Little_mett       -> ArchPaladin      (ListenTaunter)
    // Role 1: LeesDragon Prime  -> Lord of Order    (TruthTaunter1)
    // Role 2: MrPresident BR    -> StoneCrusher     (TruthTaunter2)
    // Role 3: bengt of worlds   -> Verus DoomKnight (TruthTaunter3 + Coordinator + Decay)
    private const string ListenTaunterClass = "ArchPaladin";
    private const string TruthTaunter1Class = "Lord of Order";
    private const string TruthTaunter2Class = "StoneCrusher";
    private const string TruthTaunter3Class = "Verus DoomKnight";

    private static readonly string[][] UltraClassesByRole =
    {
        new[] { ListenTaunterClass },
        new[] { TruthTaunter1Class },
        new[] { TruthTaunter2Class },
        new[] { TruthTaunter3Class }
    };

    private static readonly Dictionary<string, string> PreferredAssignments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Little_mett"] = ListenTaunterClass,
        ["LeesDragon Prime"] = TruthTaunter1Class,
        ["MrPresident BR"] = TruthTaunter2Class,
        ["bengt of worlds"] = TruthTaunter3Class
    };

    private const string TruthTurnSyncFile = "ultra_speaker_truth_turn.sync";
    private const string ListenTurnSyncFile = "ultra_speaker_listen_turn.sync";
    private const string EqualDecisionSyncFile = "ultra_speaker_equal_decision.sync";
    private const string CompletionSyncFile = "ultra_speaker_completion.sync";

    private int _truthTauntTurn;
    private int _lastTruthTauntActedTurn;
    private int _listenTauntTurn;
    private int _lastListenTauntActedTurn;
    private long _lastProcessedZoneDecisionId;
    private long _scriptStartedAtMs;
    private int _myUid;
    private int _cachedRoleIndex = -1;

    // Packet-driven self & prompt state (zero Flash Self.Auras IPC during combat)
    private readonly object _stateLock = new();
    private int _cachedHp = 5000;
    private int _cachedBossHp = 10_000_000;
    private bool _cachedAlive = true;
    private float _corruptionStacks;
    private float _somberStacks;
    private DateTime _magiaBurnUntilUtc = DateTime.MinValue;
    private DateTime _stasisUntilUtc = DateTime.MinValue;
    private DateTime _sanctityUntilUtc = DateTime.MinValue;
    private DateTime _enrageCooldownUntilUtc = DateTime.MinValue;
    private DateTime _suppressCombatSkillsUntilUtc = DateTime.MinValue;
    private long _lastEqualAtMs;
    private int _outsideCellStreak;

    private volatile bool _listenPromptTaunted;
    private volatile bool _truthPromptTaunted;
    private volatile bool _pendingDecay;
    private volatile bool _movingToEqualize;
    private volatile bool _positioned;
    private volatile bool _inBossCell;
    private DateTime _nextBossRejoinAttemptUtc = DateTime.MinValue;
    private DateTime _nextSafeWalkAttemptUtc = DateTime.MinValue;
    private long _lastListenPromptSeenMs;
    private long _lastTruthPromptSeenMs;
    private int _tauntLockFlag;
    private int _decayCastFlag;

    private const int EqualizeX = 200;
    private const int EqualizeY = 380;
    private static string _fbsMuteFile = "";

    public void ScriptMain(IScriptInterface bot)
    {
        RunBoss();
        Bot.StopSync();
    }

    public void RunBoss()
    {
        _scriptStartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        C.SetOptions(true);
        // CoreBots.SetOptions(true) starts Bot.Skills.StartAdvanced() for the equipped class;
        // stop it immediately so it never wastes VDK Unleash Doom (Decay) or AP Sacred Magic: Eden.
        Bot.Skills.Stop();
        _fbsMuteFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Skua",
            "fbs_mute.sync"
        );
        try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }

        Bot.Events.ExtensionPacketReceived += SpeakerMessageListener;
        try
        {
            Prep();
            Fight();
        }
        finally
        {
            Bot.Events.ExtensionPacketReceived -= SpeakerMessageListener;
            try { if (File.Exists(_fbsMuteFile)) File.Delete(_fbsMuteFile); } catch { }
            Engine.DisableSkills();
            C.SetOptions(false);
        }
    }

    private int ResolveRoleIndex()
    {
        string? className = Bot.Player?.CurrentClass?.Name;
        if (className == ListenTaunterClass) return 0;
        if (className == TruthTaunter1Class) return 1;
        if (className == TruthTaunter2Class) return 2;
        if (className == TruthTaunter3Class) return 3;
        return -1;
    }

    private int MyRoleIndex() => _cachedRoleIndex >= 0 ? _cachedRoleIndex : ResolveRoleIndex();

    private int MyTruthIndex()
    {
        int role = MyRoleIndex();
        return role >= 1 && role <= 3 ? role - 1 : -1;
    }

    private void EquipPresetClasses()
    {
        C.Logger($"[UltraSpeaker] Equipping role-based classes for {ArmySize} accounts.");
        UltraCustomClassSync.CustomClassSync(
            Ultra,
            Bot,
            UltraClassesByRole,
            ArmySize,
            "ultra_speaker_class.sync",
            allowDuplicates: false,
            preferredUsernameAssignments: PreferredAssignments
        );
    }

    private void ApplyAccountSafeEnhancements()
    {
        var adv = new CoreAdvanced();
        string cls = Bot.Player?.CurrentClass?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(cls))
            return;

        bool hasValiance = C.isCompletedBefore(8741, log: false);
        bool hasLacerate = C.isCompletedBefore(8739, log: false);
        bool hasPenitence = C.isCompletedBefore(8822, log: false);
        bool hasForgeHelm = C.isCompletedBefore(8828, log: false);

        WeaponSpecial w = hasValiance ? WeaponSpecial.Valiance
            : (hasLacerate ? WeaponSpecial.Lacerate : WeaponSpecial.Awe_Blast);
        CapeSpecial c = hasPenitence ? CapeSpecial.Penitence : CapeSpecial.None;
        HelmSpecial h = hasForgeHelm ? HelmSpecial.Forge : HelmSpecial.None;

        EnhancementType statType = string.Equals(cls, TruthTaunter1Class, StringComparison.OrdinalIgnoreCase)
            ? EnhancementType.Healer
            : EnhancementType.Lucky;

        C.Logger($"[UltraSpeaker] Applying {statType} enhancements for {cls} (W={w}, H={h}, C={c}).");
        adv.EnhanceEquipped(
            type: statType,
            hSpecial: h,
            wSpecial: w,
            cSpecial: c
        );
    }

    private void Prep()
    {
        UltraGeneral.EquipWarriorClass();
        Bot.Sleep(1500);
        EquipPresetClasses();
        Bot.Sleep(1500);
        ApplyAccountSafeEnhancements();

        string[] roleNames = { "ListenTaunter (AP)", "TruthTaunter1 (LoO)", "TruthTaunter2 (SC)", "TruthTaunter3 (VDK)" };
        _cachedRoleIndex = ResolveRoleIndex();
        C.Logger($"[UltraSpeaker] Assigned role: {(_cachedRoleIndex >= 0 ? roleNames[_cachedRoleIndex] : "Unknown")}");
    }

    private void Fight()
    {
        const string waitSyncPrep = "ultra_speaker_prep.sync";
        const string waitSyncBoss = "ultra_speaker_boss.sync";

        Bot.Options.DisableCollisions = true;
        Bot.Drops.Add(BossDefeatedItem);
        Bot.Drops.Add("Malgor Insignia");
        Bot.Drops.Add("Body Tonic");
        Bot.Drops.Add("Scroll of Enrage");

        if (!Bot.Quests.IsUnlocked(WeeklyQuestId))
            Bot.Quests.UpdateQuest(9125);

        if (Bot.Quests.IsDailyComplete(WeeklyQuestId))
            C.Logger("[UltraSpeaker] Weekly quest 9173 appears already completed; assisting party.");
        else if (!UltraGeneral.IsQuestGreen(Bot, WeeklyQuestId))
            UltraGeneral.EnsureAcceptOnce(Bot, WeeklyQuestId);

        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(TruthTurnSyncFile));
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(ListenTurnSyncFile));
        Ultra.ClearSyncFile(Ultra.ResolveSyncPath(CompletionSyncFile));
        try
        {
            string eqPath = Ultra.ResolveSyncPath(EqualDecisionSyncFile);
            if (File.Exists(eqPath)) File.Delete(eqPath);
        }
        catch { }

        EnsureBodyTonicIfReagents();
        if (Bot.Player.Gold >= 5_500_000 && !Bot.Inventory.Contains("Potent Revitalize Elixir", 2))
            Pots.EnsureRecommendedPotions(desiredQuant: 2, skipThird: true, context: "Speaker");

        if (!C.CheckInventory("Scroll of Enrage", 45, toInv: true))
            Scrolls.GetScrollOfEnrage();

        C.Join("Whitemap");
        WaitForPartySync(waitSyncPrep, useSkill: false);

        Pots.UseRecommendedPotions(desiredQuant: 1, skipThird: true, context: "Speaker", ensureStock: false);
        if (!Bot.Inventory.Contains("Body Tonic", 1))
            UsePotionIfOwned("Fate Tonic");
        if (!Bot.Inventory.Contains("Potent Revitalize Elixir", 1))
            UsePotionIfOwned("Divine Elixir");

        C.Logger("[UltraSpeaker] Equipping Scroll of Enrage.");
        if (!C.CheckInventory("Scroll of Enrage", 1, toInv: true))
            C.Logger("[UltraSpeaker] WARNING: Scroll of Enrage missing in Inventory/Bank!");
        for (int attempt = 0; attempt < 3 && !Bot.Inventory.IsEquipped("Scroll of Enrage"); attempt++)
        {
            Engine.EquipEnrage();
            Bot.Sleep(500);
        }
        C.Logger($"[UltraSpeaker] Scroll of Enrage equipped={Bot.Inventory.IsEquipped("Scroll of Enrage")} (qty={Bot.Inventory.GetQuantity("Scroll of Enrage")}).");
        Bot.Skills.Stop();

        Engine.Join(BossMap);
        Bot.Sleep(1500);
        _myUid = Bot.Player.ID;
        C.Logger($"[UltraSpeaker] Cached player UID: {_myUid}");
        WaitForPartySync(waitSyncBoss, useSkill: false);

        Bot.Sleep(500);
        C.Jump("Boss", "Left");
        Bot.Sleep(800);
        Bot.Player.SetSpawnPoint();
        for (int t = 0; t < 12 && !Bot.ShouldExit && !Bot.Player.HasTarget; t++)
        {
            if (!Bot.Combat.Attack(1))
                Bot.Combat.Attack(BossName);
            Bot.Sleep(300);
            if ((t == 4 || t == 9) && !Bot.Player.HasTarget)
            {
                C.Jump("Enter", "Spawn");
                Bot.Sleep(500);
                C.Jump("Boss", "Left");
                Bot.Sleep(700);
            }
        }
        Bot.Player.SetSpawnPoint();
        _cachedHp = Bot.Player?.Health > 0 ? Bot.Player.Health : 5000;
        _cachedAlive = Bot.Player?.Alive == true;
        _inBossCell = true;

        int loopCount = 0;
        int noTargetStreak = 0;
        int myRole = MyRoleIndex();

        while (!Bot.ShouldExit)
        {
            loopCount++;
            if (loopCount % 8 == 1)
                try { File.WriteAllText(_fbsMuteFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch { }

            if (_myUid <= 0)
                _myUid = Bot.Player?.ID ?? 0;

            bool aliveNow = Bot.Player?.Alive == true;
            _cachedAlive = aliveNow;
            if (aliveNow && loopCount % 5 == 1 && Bot.Player?.Health > 0)
                _cachedHp = Bot.Player.Health;

            if (!aliveNow)
            {
                _inBossCell = false;
                _outsideCellStreak = 0;
                ResetLocalAuras();
                PublishZoneStatusFromCache();
                Bot.Wait.ForTrue(() => Bot.Player?.Alive == true, 20);
                SyncTurnCountersFromFiles();
                _positioned = false;
                _movingToEqualize = false;
                _pendingDecay = false;
            }

            if (loopCount % 8 == 1 || !_inBossCell)
            {
                if (!EnsureBossCell(BossMap))
                {
                    PublishZoneStatusFromCache();
                    Bot.Sleep(250);
                    continue;
                }
            }

            _inBossCell = true;
            PublishZoneStatusFromCache();
            ProcessZoneDecision();

            if (!_movingToEqualize && aliveNow && DateTime.UtcNow >= _stasisUntilUtc)
            {
                int curX = Bot.Player?.X ?? 999;
                if (curX <= 150)
                {
                    _positioned = true;
                }
                else if (DateTime.UtcNow >= _nextSafeWalkAttemptUtc)
                {
                    _positioned = false;
                    _nextSafeWalkAttemptUtc = DateTime.UtcNow.AddMilliseconds(900);
                    int randomX = Random.Shared.Next(0, 95);
                    int randomY = Random.Shared.Next(485, 500);
                    Bot.Player?.WalkTo(randomX, randomY);
                }
            }

            if (loopCount % 20 == 0 || _cachedBossHp <= 250_000)
            {
                bool hasTurnInItem = Bot.Inventory.Contains(BossDefeatedItem, 1) || Bot.TempInv.Contains(BossDefeatedItem, 1);
                if (SyncAndCountReadyUsers(CompletionSyncFile, isReady: hasTurnInItem) >= ArmySize)
                {
                    C.Logger("[UltraSpeaker] The First Speaker defeated by party. Finishing quest 9173.");
                    Bot.Combat.CancelAutoAttack();
                    Bot.Combat.CancelTarget();
                    C.Jump("Enter", "Spawn");
                    Bot.Player?.SetSpawnPoint();
                    Bot.Sleep(1500);
                    Ultra.PersistentJoinHouse();
                    if (!Bot.Quests.IsDailyComplete(WeeklyQuestId))
                    {
                        UltraGeneral.CompleteQuest(Bot, WeeklyQuestId);
                        Bot.Sleep(2000);
                    }
                    C.Logger($"[UltraSpeaker] Malgor Insignia total: {Bot.Inventory.GetQuantity("Malgor Insignia")}");
                    break;
                }
            }

            bool hasTarget = Bot.Player?.HasTarget == true;
            if (!_movingToEqualize && Bot.Player != null && (!hasTarget || (loopCount % 10 == 0 && !string.Equals(Bot.Player.Target?.Name, BossName, StringComparison.OrdinalIgnoreCase))))
            {
                if (!Bot.Combat.Attack(1))
                    Bot.Combat.Attack(BossName);
                hasTarget = Bot.Player.HasTarget;
            }

            if (!_movingToEqualize && aliveNow && _inBossCell && !hasTarget)
            {
                noTargetStreak++;
                if (noTargetStreak >= 16)
                {
                    C.Logger("[UltraSpeaker] Boss target stuck missing in Boss cell; refreshing Enter -> Boss.");
                    noTargetStreak = 0;
                    _positioned = false;
                    C.Jump("Enter", "Spawn");
                    Bot.Sleep(450);
                    C.Jump("Boss", "Left");
                    Bot.Sleep(700);
                    Bot.Player?.SetSpawnPoint();
                    continue;
                }
            }
            else
            {
                noTargetStreak = 0;
            }

            if (aliveNow && hasTarget)
                CastSpeakerSkills(myRole);

            Bot.Sleep(120);
        }
    }

    private bool TryCastDecay(string source)
    {
        if (!_pendingDecay || Bot.Player?.HasTarget != true)
            return false;
        if (Interlocked.CompareExchange(ref _decayCastFlag, 1, 0) != 0)
            return false;

        try
        {
            if (_pendingDecay && Engine.Cast(4))
            {
                _pendingDecay = false;
                C.Logger($"[UltraSpeaker] VDK applied Decay (Unleash Doom) on Equal ({source}).");
                return true;
            }
            return false;
        }
        finally
        {
            Volatile.Write(ref _decayCastFlag, 0);
        }
    }

    private void CastSpeakerSkills(int myRole)
    {
        // VDK: during Equalize, hold GCD exclusively for Unleash Doom (Decay = Skill 4)
        if (myRole == 3 && _pendingDecay)
        {
            TryCastDecay("loop");
            return;
        }

        // If this account just claimed an upcoming Listen/Truth prompt, hold 1-4 briefly so Scroll of Enrage fires with zero GCD delay!
        if (DateTime.UtcNow < _suppressCombatSkillsUntilUtc)
            return;

        if (myRole == 3)
        {
            long sinceEqual = _lastEqualAtMs > 0
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastEqualAtMs
                : 0;
            // Hold normal GCD right before ~19.5s Equalize so Unleash Doom (Decay) fires with zero delay!
            if (sinceEqual >= 17200 && sinceEqual <= 21500)
                return;

            if (Bot.Player.MaxHealth > 0 && Bot.Player.Health < Bot.Player.MaxHealth * 0.72 && Bot.Skills.CanUseSkill(2))
                Engine.Cast(2);
            else if (Bot.Skills.CanUseSkill(1))
                Engine.Cast(1);
            else if (Bot.Skills.CanUseSkill(3))
                Engine.Cast(3);
            else if (Bot.Skills.CanUseSkill(2))
                Engine.Cast(2);
            return;
        }

        if (myRole == 0) // ArchPaladin: Hymn (2) heal/def -> Righteous Seal (3) -> Commandment (1); never OOMs.
        {
            if (Bot.Skills.CanUseSkill(2))
                Engine.Cast(2);
            else if (Bot.Skills.CanUseSkill(3))
                Engine.Cast(3);
            else if (Bot.Skills.CanUseSkill(1))
                Engine.Cast(1);
            return;
        }

        if (myRole == 1) // Lord of Order: Resurgence (2) -> Clarity (3) -> Order (4) -> Strike (1).
        {
            if (Bot.Skills.CanUseSkill(2))
                Engine.Cast(2);
            else if (Bot.Skills.CanUseSkill(3))
                Engine.Cast(3);
            else if (Bot.Skills.CanUseSkill(4))
                Engine.Cast(4);
            else if (Bot.Skills.CanUseSkill(1))
                Engine.Cast(1);
            return;
        }

        if (myRole == 2) // StoneCrusher: Land's Embrace (4) HoT/DR -> Echoing Earth (2) -> Magnitude (3) -> Tremor (1).
        {
            if (Bot.Skills.CanUseSkill(4))
                Engine.Cast(4);
            else if (Bot.Skills.CanUseSkill(2))
                Engine.Cast(2);
            else if (Bot.Skills.CanUseSkill(3))
                Engine.Cast(3);
            else if (Bot.Skills.CanUseSkill(1))
                Engine.Cast(1);
        }
    }

    private void ResetLocalAuras()
    {
        lock (_stateLock)
        {
            _corruptionStacks = 0f;
            _somberStacks = 0f;
            _magiaBurnUntilUtc = DateTime.MinValue;
            _stasisUntilUtc = DateTime.MinValue;
            _sanctityUntilUtc = DateTime.MinValue;
        }
    }

    private void EnsureBodyTonicIfReagents()
    {
        if (Bot.Inventory.Contains("Body Tonic", 1))
            return;

        if (!C.CheckInventory(new[] { "Roc Tongue", "Chaoroot", "Dragon Runestone" }))
            return;

        if (!Bot.Inventory.TryGetItem("Roc Tongue", out InventoryItem? roc) || roc == null
            || !Bot.Inventory.TryGetItem("Chaoroot", out InventoryItem? chao) || chao == null)
            return;

        C.Logger("[UltraSpeaker] Crafting Body Tonic directly from stocked reagents.");
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

    private void SyncTurnCountersFromFiles()
    {
        int remoteTruth = ReadMaxTurnFromSyncFile(TruthTurnSyncFile);
        if (remoteTruth > _truthTauntTurn)
        {
            _truthTauntTurn = remoteTruth;
            _lastTruthTauntActedTurn = remoteTruth;
            C.Logger($"[UltraSpeaker] Re-synced truth turn to {_truthTauntTurn} after respawn.");
        }

        int remoteListen = ReadMaxTurnFromSyncFile(ListenTurnSyncFile);
        if (remoteListen > _listenTauntTurn)
        {
            _listenTauntTurn = remoteListen;
            _lastListenTauntActedTurn = remoteListen;
            C.Logger($"[UltraSpeaker] Re-synced listen turn to {_listenTauntTurn} after respawn.");
        }
    }

    private int ReadMaxTurnFromSyncFile(string fileName)
    {
        int maxTurn = 0;
        try
        {
            string[] lines = Ultra.ReadLines(Ultra.ResolveSyncPath(fileName));
            foreach (string line in lines)
            {
                string[] parts = line.Split(':');
                if (parts.Length >= 3 && int.TryParse(parts[1], out int turn) && turn > maxTurn)
                    maxTurn = turn;
            }
        }
        catch { }
        return maxTurn;
    }

    private int SyncAndCountReadyUsers(string syncFileName, bool isReady)
    {
        string syncPath = Ultra.ResolveSyncPath(syncFileName);
        string username = (Bot.Player?.Username ?? "Unknown").Replace(":", "-");
        string className = (Bot.Player?.CurrentClass?.Name ?? "Unknown").Replace(":", "-");
        Ultra.UpdateEntry(syncPath, $"{username}|{className}", isReady ? "1" : "0");

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
                C.Logger($"[UltraSpeaker] Party sync ({syncFileName}): {ready}/{ArmySize}");
            }

            if (ready >= ArmySize)
                break;

            Bot.Sleep(400);
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
                Bot.Sleep(400);
            }
        }
    }

    private bool EnsureBossCell(string map)
    {
        string currentMap = Bot.Map?.Name?.Split('-')[0] ?? string.Empty;
        string currentCell = Bot.Player?.Cell ?? string.Empty;
        if (currentMap.Equals(map, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentCell, "Boss", StringComparison.OrdinalIgnoreCase))
        {
            _outsideCellStreak = 0;
            return true;
        }

        // Ignore single transient empty/IPC glitch readings unless persistent or dead.
        if (string.IsNullOrEmpty(currentMap) || string.IsNullOrEmpty(currentCell))
        {
            _outsideCellStreak++;
            if (_outsideCellStreak < 3)
                return _inBossCell;
        }

        if (DateTime.UtcNow < _nextBossRejoinAttemptUtc)
            return false;

        _nextBossRejoinAttemptUtc = DateTime.UtcNow.AddSeconds(5);
        _outsideCellStreak = 0;
        C.Logger($"[UltraSpeaker] Outside boss cell (map={currentMap}, cell={currentCell}); rejoining {map}/Boss.");
        Engine.Join(map, "Boss", "Left");

        bool returned = Bot.Wait.ForTrue(
            () => (Bot.Map?.Name?.Split('-')[0] ?? string.Empty).Equals(map, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Bot.Player?.Cell, "Boss", StringComparison.OrdinalIgnoreCase),
            12
        );

        if (returned)
        {
            _positioned = false;
            _movingToEqualize = false;
            _inBossCell = true;
            C.Logger("[UltraSpeaker] Rejoined Boss cell.");
        }
        return returned;
    }

    private void SpeakerMessageListener(dynamic packet)
    {
        try
        {
            string type = packet["params"].type;
            if (type is not "json")
                return;

            dynamic dataObj = packet["params"].dataObj;
            string cmd = dataObj.cmd.ToString();
            if (cmd != "ct")
                return;

            JObject? ctObj = dataObj as JObject ?? JObject.FromObject(dataObj);
            if (ctObj == null)
                return;

            UpdatePacketState(ctObj);

            JArray? anims = ctObj["anims"] as JArray;
            if (anims == null)
                return;

            foreach (JToken anim in anims)
            {
                string? message = anim["msg"]?.ToString();
                if (string.IsNullOrEmpty(message))
                    continue;

                if (message.Contains("You shall listen.", StringComparison.OrdinalIgnoreCase))
                {
                    long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (nowMs - _lastListenPromptSeenMs < 3500)
                        continue;

                    _lastListenPromptSeenMs = nowMs;
                    _listenPromptTaunted = false;
                    int turn = Interlocked.Increment(ref _listenTauntTurn);
                    if (MyRoleIndex() == 3)
                    {
                        _ = Task.Run(() =>
                        {
                            try { Ultra.UpdateEntry(Ultra.ResolveSyncPath(ListenTurnSyncFile), "Coord", turn.ToString()); } catch { }
                        });
                    }
                    C.Logger($"[UltraSpeaker] Detected: 'You shall listen.' (turn {turn})");
                    if (_cachedAlive)
                        _ = HandleListenTauntAsync(turn);
                }

                if (message.Contains("I will make you see the truth.", StringComparison.OrdinalIgnoreCase))
                {
                    long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (nowMs - _lastTruthPromptSeenMs < 2500)
                        continue;

                    _lastTruthPromptSeenMs = nowMs;
                    _truthPromptTaunted = false;
                    int turn = Interlocked.Increment(ref _truthTauntTurn);
                    if (MyRoleIndex() == 3)
                    {
                        _ = Task.Run(() =>
                        {
                            try { Ultra.UpdateEntry(Ultra.ResolveSyncPath(TruthTurnSyncFile), "Coord", turn.ToString()); } catch { }
                        });
                    }
                    C.Logger($"[UltraSpeaker] Detected: 'I will make you see the truth.' (turn {turn})");
                    if (_cachedAlive)
                        _ = HandleTruthTauntAsync(turn);
                }

                if (message.Contains("All stand equal beneath the eyes of the Eternal.", StringComparison.OrdinalIgnoreCase))
                {
                    _lastEqualAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (MyRoleIndex() == 3 && _cachedAlive)
                    {
                        _pendingDecay = true;
                        _ = DecayAsync();
                    }
                    PublishZoneStatusFromCache();
                    HandleEqualize();
                }
            }
        }
        catch { }
    }

    private void UpdatePacketState(JObject ctObj)
    {
        DateTime nowUtc = DateTime.UtcNow;
        string myTargetInf = _myUid > 0 ? $"p:{_myUid}" : string.Empty;
        string? myUsername = Bot.Player?.Username;

        if (ctObj["m"] is JObject mMap && mMap["1"] is JObject m1 && m1["intHP"] != null)
            _cachedBossHp = m1["intHP"]!.Value<int>();

        if (!string.IsNullOrEmpty(myUsername) && ctObj["p"] is JObject pMap)
        {
            foreach (JProperty prop in pMap.Properties())
            {
                if (!string.Equals(prop.Name, myUsername, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (prop.Value is JObject pVal)
                {
                    if (pVal["intHP"] != null)
                        _cachedHp = pVal["intHP"]!.Value<int>();
                    if (pVal["intState"] != null && pVal["intState"]!.Value<int>() == 0)
                    {
                        _cachedAlive = false;
                        ResetLocalAuras();
                    }
                }
            }
        }

        if (ctObj["a"] is not JArray aurasArray)
            return;

        bool stateChanged = false;
        foreach (JToken entry in aurasArray)
        {
            string? aCmd = entry["cmd"]?.ToString();
            string? tInf = entry["tInf"]?.ToString();

            if (tInf == "m:1" && (aCmd == "aura+" || aCmd == "aura++") && entry["auras"] is JArray mAuras)
            {
                foreach (JToken ma in mAuras)
                {
                    if (string.Equals(ma["nam"]?.ToString(), "Focus", StringComparison.OrdinalIgnoreCase))
                    {
                        _listenPromptTaunted = true;
                        _truthPromptTaunted = true;
                        if (!string.IsNullOrEmpty(myTargetInf) && string.Equals(entry["cInf"]?.ToString(), myTargetInf, StringComparison.OrdinalIgnoreCase))
                        {
                            _enrageCooldownUntilUtc = nowUtc.AddSeconds(13.0);
                            stateChanged = true;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(myTargetInf) || !string.Equals(tInf, myTargetInf, StringComparison.OrdinalIgnoreCase))
                continue;

            lock (_stateLock)
            {
                if ((aCmd == "aura+" || aCmd == "aura++") && entry["auras"] is JArray addedAuras)
                {
                    foreach (JToken a in addedAuras)
                    {
                        string? name = a["nam"]?.ToString();
                        if (string.IsNullOrEmpty(name))
                            continue;
                        double dur = a["dur"]?.Value<double>() ?? 20.0;
                        float stk = a["stk"]?.Value<float>() ?? 1f;
                        DateTime until = nowUtc.AddSeconds(dur + 0.5);

                        if (name.Equals("Corruption", StringComparison.OrdinalIgnoreCase))
                            _corruptionStacks = Math.Max(1f, stk);
                        else if (name.Equals("Somber", StringComparison.OrdinalIgnoreCase))
                            _somberStacks = Math.Max(1f, stk);
                        else if (name.Equals("Magia Burn", StringComparison.OrdinalIgnoreCase))
                        {
                            _magiaBurnUntilUtc = until;
                            stateChanged = true;
                        }
                        else if (name.Equals("Stasis", StringComparison.OrdinalIgnoreCase))
                        {
                            _stasisUntilUtc = until;
                            stateChanged = true;
                        }
                        else if (name.Equals("Sanctity", StringComparison.OrdinalIgnoreCase))
                        {
                            _sanctityUntilUtc = until;
                            stateChanged = true;
                        }
                    }
                }
                else if ((aCmd == "aura-" || aCmd == "aura--") && entry["aura"] is JObject rem)
                {
                    string? name = rem["nam"]?.ToString();
                    if (string.IsNullOrEmpty(name))
                        continue;

                    if (name.Equals("Corruption", StringComparison.OrdinalIgnoreCase))
                        _corruptionStacks = 0f;
                    else if (name.Equals("Somber", StringComparison.OrdinalIgnoreCase))
                        _somberStacks = 0f;
                    else if (name.Equals("Magia Burn", StringComparison.OrdinalIgnoreCase))
                    {
                        _magiaBurnUntilUtc = DateTime.MinValue;
                        stateChanged = true;
                    }
                    else if (name.Equals("Stasis", StringComparison.OrdinalIgnoreCase))
                    {
                        _stasisUntilUtc = DateTime.MinValue;
                        stateChanged = true;
                    }
                    else if (name.Equals("Sanctity", StringComparison.OrdinalIgnoreCase))
                    {
                        _sanctityUntilUtc = DateTime.MinValue;
                        stateChanged = true;
                    }
                }
            }
        }

        if (stateChanged)
            PublishZoneStatusFromCache();
    }

    private void HandleEqualize()
    {
        try
        {
            if (MyRoleIndex() != 3)
                return;

            long eventId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _ = PublishEqualDecisionAsync(eventId);
        }
        catch { }
    }

    private bool IsSafeToZoneFromCache(out float health, out float corruption, out float somber)
    {
        DateTime nowUtc = DateTime.UtcNow;
        lock (_stateLock)
        {
            health = _cachedHp;
            corruption = _corruptionStacks;
            somber = _somberStacks;
            return _cachedAlive
                && _inBossCell
                && health >= 3150f
                && nowUtc >= _magiaBurnUntilUtc
                && nowUtc >= _stasisUntilUtc
                && nowUtc >= _sanctityUntilUtc;
        }
    }

    private void PublishZoneStatusFromCache()
    {
        int role = MyRoleIndex();
        if (role < 0)
            return;

        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            DateTime nowUtc = DateTime.UtcNow;
            bool enrageReady = nowUtc >= _enrageCooldownUntilUtc;
            string status;
            lock (_stateLock)
            {
                status = string.Join("|", new[]
                {
                    now.ToString(CultureInfo.InvariantCulture),
                    _cachedAlive ? "1" : "0",
                    _inBossCell ? "1" : "0",
                    ((float)_cachedHp).ToString("R", CultureInfo.InvariantCulture),
                    _corruptionStacks.ToString("R", CultureInfo.InvariantCulture),
                    _somberStacks.ToString("R", CultureInfo.InvariantCulture),
                    nowUtc < _magiaBurnUntilUtc ? "1" : "0",
                    nowUtc < _stasisUntilUtc ? "1" : "0",
                    nowUtc < _sanctityUntilUtc ? "1" : "0",
                    _movingToEqualize ? "1" : "0",
                    enrageReady ? "1" : "0"
                });
            }
            AtomicWrite(Ultra.ResolveSyncPath($"UltraSpeakerZoneStatus_{role}.sync"), status);
        }
        catch { }
    }

    private int SelectSafeZoneRole()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var eligible = new List<(int Role, float DebuffStacks, float Health)>();

        for (int role = 0; role < ArmySize; role++)
        {
            try
            {
                string path = Ultra.ResolveSyncPath($"UltraSpeakerZoneStatus_{role}.sync");
                string[] p = File.ReadAllText(path).Split('|');
                if (p.Length < 9
                    || !long.TryParse(p[0], out long timestamp)
                    || !int.TryParse(p[1], out int alive)
                    || !int.TryParse(p[2], out int onBoss)
                    || !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float hp)
                    || !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float corruption)
                    || !float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float somber)
                    || !int.TryParse(p[6], out int burn)
                    || !int.TryParse(p[7], out int stasis)
                    || !int.TryParse(p[8], out int sanctity))
                    continue;

                bool fresh = now >= timestamp && now - timestamp <= 4000;
                if (fresh && alive == 1 && onBoss == 1 && hp >= 3150f && burn == 0 && stasis == 0 && sanctity == 0)
                    eligible.Add((role, corruption + somber, hp));
            }
            catch { }
        }

        return eligible
            .OrderBy(x => x.Role == 0 ? 1 : 0)
            .ThenByDescending(x => x.DebuffStacks > 0f ? 1 : 0)
            .ThenByDescending(x => x.DebuffStacks)
            .ThenByDescending(x => x.Health)
            .Select(x => x.Role)
            .FirstOrDefault(-1);
    }

    private async Task PublishEqualDecisionAsync(long eventId)
    {
        await Task.Delay(180);
        int selectedRole = SelectSafeZoneRole();
        string decisionPath = Ultra.ResolveSyncPath(EqualDecisionSyncFile);
        try
        {
            long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            AtomicWrite(decisionPath, $"{eventId}|{selectedRole}|{issuedAt}");
            string choice = selectedRole >= 0 ? $"role {selectedRole}" : "nobody (all blocked)";
            C.Logger($"[UltraSpeaker] Equal event {eventId}: selected {choice}.");
        }
        catch (Exception ex)
        {
            C.Logger($"[UltraSpeaker] Could not publish Equal decision ({ex.Message}).");
        }
    }

    private void ProcessZoneDecision()
    {
        if (_movingToEqualize || !_cachedAlive)
            return;

        try
        {
            string path = Ultra.ResolveSyncPath(EqualDecisionSyncFile);
            if (!File.Exists(path))
                return;

            string[] p = File.ReadAllText(path).Split('|');
            if (p.Length != 3
                || !long.TryParse(p[0], out long eventId)
                || !int.TryParse(p[1], out int selectedRole)
                || !long.TryParse(p[2], out long issuedAt)
                || eventId <= _lastProcessedZoneDecisionId
                || issuedAt < _scriptStartedAtMs)
                return;

            _lastProcessedZoneDecisionId = eventId;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (selectedRole != MyRoleIndex() || now < issuedAt || now - issuedAt > 2500)
                return;

            if (!IsSafeToZoneFromCache(out float health, out float corruption, out float somber))
            {
                C.Logger($"[UltraSpeaker] Selected for Equal event {eventId}, but safety check failed; staying safe.");
                return;
            }

            C.Logger($"[UltraSpeaker] Event {eventId}: soaking Equal zone (HP={health:F0}, corr={corruption:F1}, somb={somber:F1}).");
            _movingToEqualize = true;
            _ = Task.Run(() =>
            {
                try
                {
                    Bot.Player?.WalkTo(EqualizeX, EqualizeY);
                    Bot.Wait.ForTrue(() => Bot.Player != null && (Math.Abs(Bot.Player.X - EqualizeX) < 35 || !Bot.Player.Alive), 8);

                    DateTime maxHoldUntil = DateTime.UtcNow.AddMilliseconds(3500);
                    while (!Bot.ShouldExit && _cachedAlive && DateTime.UtcNow < maxHoldUntil)
                    {
                        bool gotSanctity;
                        lock (_stateLock)
                            gotSanctity = DateTime.UtcNow < _sanctityUntilUtc;
                        if (gotSanctity)
                        {
                            Bot.Sleep(200);
                            break;
                        }
                        Bot.Sleep(80);
                    }

                    if (_cachedAlive && _inBossCell)
                    {
                        C.Logger($"[UltraSpeaker] Event {eventId}: returning to safe box.");
                        int homeX = Random.Shared.Next(0, 95);
                        int homeY = Random.Shared.Next(485, 500);
                        Bot.Player?.WalkTo(homeX, homeY);
                        Bot.Wait.ForTrue(() => Bot.Player != null && (Math.Abs(Bot.Player.X - homeX) < 40 || !Bot.Player.Alive), 4);
                    }
                }
                finally
                {
                    _movingToEqualize = false;
                }
            });
        }
        catch { }
    }

    private readonly struct TauntSnapshot
    {
        public readonly int Role;
        public readonly bool Eligible;
        public readonly bool EnrageReady;
        public readonly bool HasSanctity;
        public readonly bool HasMagiaBurn;

        public TauntSnapshot(int role, bool eligible, bool enrageReady, bool hasSanctity, bool hasMagiaBurn)
        {
            Role = role;
            Eligible = eligible;
            EnrageReady = enrageReady;
            HasSanctity = hasSanctity;
            HasMagiaBurn = hasMagiaBurn;
        }
    }

    private TauntSnapshot ReadTauntSnapshot(int role)
    {
        try
        {
            string path = Ultra.ResolveSyncPath($"UltraSpeakerZoneStatus_{role}.sync");
            string[] p = File.ReadAllText(path).Split('|');
            if (p.Length < 11
                || !long.TryParse(p[0], out long ts)
                || !int.TryParse(p[1], out int alive)
                || !int.TryParse(p[2], out int onBoss)
                || !int.TryParse(p[6], out int burn)
                || !int.TryParse(p[7], out int stasis)
                || !int.TryParse(p[8], out int sanctity)
                || !int.TryParse(p[9], out int moving)
                || !int.TryParse(p[10], out int enrageReady))
                return new TauntSnapshot(role, false, false, false, false);

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool basic = now >= ts && now - ts <= 4000 && alive == 1 && onBoss == 1 && stasis == 0 && moving == 0;
            return new TauntSnapshot(role, basic, enrageReady == 1, sanctity == 1, burn == 1);
        }
        catch
        {
            return new TauntSnapshot(role, false, false, false, false);
        }
    }

    private async Task<bool> TryFireTauntWindowAsync(string label, int turn, int windowMs, bool isTruthPrompt)
    {
        if (Interlocked.CompareExchange(ref _tauntLockFlag, 1, 0) != 0)
            return false;

        try
        {
            _suppressCombatSkillsUntilUtc = DateTime.UtcNow.AddMilliseconds(Math.Min(windowMs, 1050));
            DateTime end = DateTime.UtcNow.AddMilliseconds(windowMs);
            while (!Bot.ShouldExit && _cachedAlive && DateTime.UtcNow < end)
            {
                if (isTruthPrompt ? _truthPromptTaunted : _listenPromptTaunted)
                    return false;

                if (Bot.Player == null || !Bot.Player.HasTarget || !string.Equals(Bot.Player.Target?.Name, BossName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!Bot.Combat.Attack(1))
                        Bot.Combat.Attack(BossName);
                    await Task.Delay(40);
                    continue;
                }

                if (DateTime.UtcNow >= _enrageCooldownUntilUtc && Bot.Skills.CanUseSkill(5) && Engine.Cast(5))
                {
                    _enrageCooldownUntilUtc = DateTime.UtcNow.AddSeconds(13.0);
                    PublishZoneStatusFromCache();
                    _suppressCombatSkillsUntilUtc = DateTime.MinValue;
                    C.Logger($"[UltraSpeaker] Taunted {label} turn {turn} (Scroll of Enrage).");
                    return true;
                }
                await Task.Delay(35);
            }
            return false;
        }
        finally
        {
            _suppressCombatSkillsUntilUtc = DateTime.MinValue;
            Volatile.Write(ref _tauntLockFlag, 0);
        }
    }

    private async Task HandleListenTauntAsync(int turn)
    {
        int myRole = MyRoleIndex();
        if (myRole < 0)
            return;

        // Dynamically rank Listen taunters with Enrage ready first: AP (0) -> SC (2) -> LoO (1) -> VDK (3, keep unstunned for Equal Decay).
        // Among non-AP backups, prefer those already holding Sanctity so clean Equal soakers stay free of Stasis.
        var candidates = new[]
        {
            ReadTauntSnapshot(0),
            ReadTauntSnapshot(1),
            ReadTauntSnapshot(2),
            ReadTauntSnapshot(3)
        }
        .OrderByDescending(s => s.Eligible && s.EnrageReady ? 1 : 0)
        .ThenByDescending(s => s.Role != 3 && (s.Role == 0 || s.HasSanctity) ? 1 : 0)
        .ThenBy(s => s.Role switch { 0 => 0, 2 => 1, 1 => 2, _ => 3 })
        .ToList();

        int myRank = candidates.FindIndex(s => s.Role == myRole);
        if (myRank < 0)
            return;

        if (myRank == 0)
        {
            if (turn <= _lastListenTauntActedTurn)
                return;
            _lastListenTauntActedTurn = turn;
            if (DateTime.UtcNow >= _enrageCooldownUntilUtc)
                await TryFireTauntWindowAsync("Listen", turn, 700, isTruthPrompt: false);
            return;
        }

        if (myRank >= 3)
            return;

        int delayMs = myRank == 1 ? 1400 : 1680;
        await Task.Delay(delayMs);

        if (!_cachedAlive || _listenPromptTaunted || !_inBossCell || DateTime.UtcNow < _enrageCooldownUntilUtc)
            return;

        await TryFireTauntWindowAsync($"Listen (BACKUP-{myRank})", turn, 550, isTruthPrompt: false);
    }

    private async Task HandleTruthTauntAsync(int turn)
    {
        int myTruthIdx = MyTruthIndex();
        if (myTruthIdx < 0)
            return;

        int myRole = myTruthIdx + 1;
        // Build Sanctity-first priority order so players ALREADY holding Sanctity absorb Magia Burn, keeping non-Sanctity soakers clean!
        var candidates = new[]
        {
            ReadTauntSnapshot(1),
            ReadTauntSnapshot(2),
            ReadTauntSnapshot(3)
        }
        .OrderByDescending(s => s.Eligible && s.EnrageReady ? 1 : 0)
        .ThenByDescending(s => s.HasSanctity ? 1 : 0)
        .ThenBy(s => s.HasMagiaBurn ? 1 : 0)
        .ThenBy(s => ((s.Role - 1) - (turn % 3) + 3) % 3)
        .ToList();

        int myRank = candidates.FindIndex(s => s.Role == myRole);
        if (myRank < 0)
            return;

        if (myRank == 0)
        {
            if (turn <= _lastTruthTauntActedTurn)
                return;
            _lastTruthTauntActedTurn = turn;
            if (DateTime.UtcNow >= _enrageCooldownUntilUtc)
                await TryFireTauntWindowAsync("Truth", turn, 700, isTruthPrompt: true);
            return;
        }

        int delayMs = myRank == 1 ? 1400 : 1680;
        await Task.Delay(delayMs);

        if (!_cachedAlive || _truthPromptTaunted || !_inBossCell || DateTime.UtcNow < _enrageCooldownUntilUtc)
            return;

        await TryFireTauntWindowAsync($"Truth (BACKUP-{myRank})", turn, 550, isTruthPrompt: true);
    }

    private async Task DecayAsync()
    {
        if (MyRoleIndex() != 3)
            return;

        for (int i = 0; i < 80 && !Bot.ShouldExit && _cachedAlive && _pendingDecay; i++)
        {
            if (TryCastDecay("async"))
                return;
            await Task.Delay(25);
        }
    }

    private static void AtomicWrite(string path, string contents)
    {
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, true);
    }
}
