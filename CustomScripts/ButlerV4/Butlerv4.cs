/*
name: Butlerv4 (TCP)
description: Follows a leader via Goto every ~1s. Connects via TCP for leader location data.
tags: butler, follow, goto, tcp
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include /home/max/Skua-Linux/CustomScripts/HDBoN/HDBoNConsumables.cs

using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class Butlerv4
{
    private static IScriptInterface Bot => IScriptInterface.Instance;
    private static CoreBots Core => CoreBots.Instance;

    private static CoreAdvanced Adv
    {
        get => _Adv ??= new CoreAdvanced();
        set => _Adv = value;
    }
    private static CoreAdvanced _Adv;

    public bool DontPreconfigure = true;
    public string OptionsStorage = "Butler1";

    public List<IOption> Options = new()
    {
        new Option<string>("Leader1Name", "Leader 1 Name", "Name of leader 1.", ""),
        new Option<string>("Leader1Butlers", "Butlers For Leader 1", "Comma-separated butler account names. Example: acc1,acc2,acc3", ""),
        new Option<string>("Leader2Name", "Leader 2 Name", "Name of leader 2.", ""),
        new Option<string>("Leader2Butlers", "Butlers For Leader 2", "Comma-separated butler account names. Example: acc1,acc2,acc3", ""),
        new Option<string>("Leader3Name", "Leader 3 Name", "Name of leader 3.", ""),
        new Option<string>("Leader3Butlers", "Butlers For Leader 3", "Comma-separated butler account names. Example: acc1,acc2,acc3", ""),
        new Option<string>("Leader4Name", "Leader 4 Name", "Name of leader 4.", ""),
        new Option<string>("Leader4Butlers", "Butlers For Leader 4", "Comma-separated butler account names. Example: acc1,acc2,acc3", ""),
        new Option<bool>("AutoEnhance", "Auto Enhance", "Automatically enhance equipped class on startup.", true),
        new Option<string>("ClassOverrides", "Per-account class overrides", "Account=class pairs separated by semicolons.", ""),
        new Option<string>("BossGearOverrides", "Per-account boss gear", "Account=item|item pairs separated by semicolons.", ""),
        new Option<string>("SkipClassPotionCraft", "Skip crafting class potions", "Comma-separated accounts that should use their stocked tonics and elixirs.", ""),
        new Option<bool>("PrepareBossPotions", "Prepare boss potions", "Stock the class-recommended potions for leader fights.", true),
        new Option<bool>("UseGoto", "Use Goto", "Use Goto to follow instead of direct Join+Jump.", true),
        CoreBots.Instance.SkipOptions,
    };

    string playerName = string.Empty;
    private volatile bool _gotoPending;
    private DateTime _lastGotoTime = DateTime.MinValue;
    private const int GotoMinIntervalMs = 2500;

    // ── TCP state ────────────────────────────────────────────────────
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private System.IO.StreamReader? _streamReader;
    private string _tcpMap = "";
    private string _tcpRoom = "";
    private string _tcpCell = "";
    private string _tcpPad = "";
    private bool _tcpInCombat = false;
    private bool _tcpHasTarget = false;
    private bool _lockedZone = false;
    private bool _roomFull = false;
    private bool _pvpZone = false;
    private bool _gotoIgnored = false;
    private bool _differentServer = false;
    private bool _isParked = false;
    private bool _houseJoined = false;
    private bool _leaderPortLookupFailedLogged = false;
    private bool _wasAlive = true;
    private readonly HDBoNConsumables _consumables = new();
    private bool _hdbon;
    private bool _preparePotions;
    private bool _engagingLeaderTarget;
    private DateTime _nextTargetCheck;
    private string _visibleBoss = "";
    private bool _partyPrepared;
    private string _partyStatusFile = "";
    private string _lastPartyStatus = "";
    private DateTime _lastPartyWrite;
    private const string PotionPrepLockFile = "/home/max/.config/Skua/logs/hdbon_potion_prep.lock";

    public void ScriptMain(IScriptInterface bot)
    {
        Core.SetOptions(disableClassSwap: true);

        Bot.Events.ExtensionPacketReceived += ChatListener;

        string myUsername = Bot.Player.Username ?? "";
        _partyStatusFile = Path.Combine("/home/max/.config/Skua/logs", $"hdbon_party_{SafeAccountSlug(myUsername)}.status");
        PublishPartyStatus("PREPARING", "", "");

        for (int i = 1; i <= 4; i++)
        {
            string butlerList = Bot.Config!.Get<string>($"Leader{i}Butlers") ?? "";
            var butlers = butlerList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (butlers.Any(b => string.Equals(b, myUsername, StringComparison.OrdinalIgnoreCase)))
            {
                playerName = Bot.Config!.Get<string>($"Leader{i}Name") ?? "";
                break;
            }
        }

        if (string.IsNullOrEmpty(playerName))
        {
            Core.Logger($"This account is not assigned to any leader's butler list.", messageBox: true, stopBot: true);
            return;
        }

        ConnectToLeader();
        _hdbon = string.Equals(playerName, "Bengt of Worlds", StringComparison.OrdinalIgnoreCase);
        _preparePotions = _hdbon && Bot.Config!.Get<bool>("PrepareBossPotions");

        // This helper is dedicated to Bengt's HDBoN team. Apply account-specific
        // class and damage gear before joining the leader's boss map.
        if (_hdbon)
        {
            string? classOverride = GetAccountOverride("ClassOverrides", myUsername);
            if (!string.IsNullOrWhiteSpace(classOverride) && Core.CheckInventory(classOverride))
            {
                Core.SoloClass = classOverride;
                Core.EquipClass(ClassType.Solo);
                Bot.Wait.ForItemEquip(classOverride);
                Core.Logger($"[Butler] HDBoN class set to {classOverride}.");
            }

        }

        if (_hdbon)
        {
            string? gearOverride = GetAccountOverride("BossGearOverrides", myUsername);
            string[] gear = (gearOverride ?? "")
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (gear.Length > 0)
            {
                Core.Equip(gear);
                Core.Logger($"[Butler] HDBoN damage gear equipped: {string.Join(", ", gear)}.");
            }
        }

        // Auto-enhance equipped class if enabled
        if (Bot.Config!.Get<bool>("AutoEnhance"))
        {
            string currentClass = Bot.Player.CurrentClass?.Name ?? "";
            if (!string.IsNullOrEmpty(currentClass) && Core.CheckInventory(currentClass))
            {
                Core.Logger($"[Butler] Auto-enhancing class: {currentClass}");
                Adv.SmartEnhance(currentClass);
            }
        }

        if (_hdbon)
            _consumables.ConfigureZubamiGear();

        if (_preparePotions)
        {
            string[] skipCraft = (Bot.Config!.Get<string>("SkipClassPotionCraft") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool craftClassPotions = !skipCraft.Contains(myUsername, StringComparer.OrdinalIgnoreCase);
            using (AcquirePotionPrepLock())
            {
                if (!Bot.ShouldExit)
                    _consumables.Stock(craftClassPotions);
            }
            if (!Bot.ShouldExit)
                _consumables.Prepare();
        }

        if (_hdbon && string.Equals(Bot.Player.CurrentClass?.Name, "Void Highlord", StringComparison.OrdinalIgnoreCase))
        {
            Bot.Skills.StartAdvanced("Void Highlord", "Def", autoEquip: false);
            Core.Logger("[Butler] Using the Void Highlord defensive skill rotation.");
        }

        if (_hdbon && string.Equals(Bot.Player.CurrentClass?.Name, "ArchPaladin", StringComparison.OrdinalIgnoreCase))
        {
            Bot.Skills.StartAdvanced("3 | 2 | 1");
            Core.Logger("[Butler] ArchPaladin will keep Righteous Seal and prioritize group healing.");
        }

        _partyPrepared = true;
        PublishPartyStatus("WAIT", "", "");

        DontAttack();
        FollowLeader();

        Bot.Events.ExtensionPacketReceived -= ChatListener;
    }

    private void FollowLeader()
    {
        while (!Bot.ShouldExit)
        {
            while (!Bot.ShouldExit && Bot.Player?.Alive != true)
            {
                if (_wasAlive)
                    Core.Logger("[Butler] Player died; waiting for respawn.");
                _wasAlive = false;
                _engagingLeaderTarget = false;
                PublishPartyStatus("WAIT", _tcpMap, _tcpRoom);
                Core.Sleep(250);
            }

            if (!_wasAlive)
                Core.Logger("[Butler] Player revived.");
            _wasAlive = true;

            PollTcpData();
            PublishCurrentPartyStatus();

            if (_isParked)
            {
                _lockedZone = false;
                _roomFull = false;
                _pvpZone = false;
                _gotoIgnored = false;
                _differentServer = false;
                Core.Sleep(5000);
                _isParked = false;
                continue;
            }

            // Leader offline check — if TCP isn't connected, park
            if (_tcp == null || !_tcp.Connected)
            {
                EnterSafeState("Leader is offline or unreachable, parking");
                continue;
            }

            if (_differentServer)
            {
                EnterSafeState("Leader could not be found. Either in a different server or logged off, parking");
                continue;
            }

            if (_lockedZone)
            {
                Core.Logger($"[Butler] Locked zone — tcpMap=[{_tcpMap}] tcpRoom=[{_tcpRoom}]");
                JoinLeaderMap(_tcpMap, _tcpRoom);
                Core.Jump(_tcpCell, _tcpPad);
                _lockedZone = false;
                continue;
            }

            if (_roomFull)
            {
                EnterSafeState("Room is full, parking");
                continue;
            }

            if (_pvpZone)
            {
                Core.Logger($"[Butler] PvP zone — tcpMap=[{_tcpMap}] tcpRoom=[{_tcpRoom}]");
                JoinLeaderMap(_tcpMap, _tcpRoom);
                Core.Jump(_tcpCell, _tcpPad);
                _pvpZone = false;
                continue;
            }

            if (_gotoIgnored)
            {
                if (Bot.Config!.Get<bool>("UseGoto"))
                {
                    Core.Logger("Please disable incognito mode in CBO for your leader or turn on its Goto", messageBox: true, stopBot: true);
                    return;
                }
                _gotoIgnored = false;
                continue;
            }

            if (int.TryParse(_tcpRoom, out int roomNum) && roomNum < 1000)
            {
                EnterSafeState($"Leader in room {roomNum}. Please choose a room number higher than 1000, parking");
                continue;
            }

            TryGotoLeader();

            if ((_tcpInCombat || _tcpHasTarget) && (!_hdbon || InLeaderPosition()))
            {
                string target = BossInCurrentMap() ?? "*";
                var currentTarget = _hdbon ? Bot.Player.Target : null;
                bool needsTarget = currentTarget?.Alive != true
                    || (target != "*" && !string.Equals(currentTarget.Name, target, StringComparison.OrdinalIgnoreCase));
                if (!_hdbon || !_engagingLeaderTarget || needsTarget)
                {
                    bool engaged = Bot.Combat.Attack(target);
                    if (!engaged && _hdbon && target != "*" && !Bot.Combat.StopAttacking)
                        engaged = Bot.Combat.SelectMonster(target);
                    if (_hdbon && engaged && !_engagingLeaderTarget)
                        Core.Logger($"[Butler] Engaged leader target: {target}.");
                    _engagingLeaderTarget = engaged;
                }

                if (_preparePotions)
                    _consumables.UseInCombat();
            }
            else
            {
                _engagingLeaderTarget = false;
                if ((Bot.Player?.InCombat == true || Bot.Player?.HasTarget == true) && !IsLeaderInSameCell())
                    QuickDeaggro();
            }

            Core.Sleep(500);
        }
    }

    private string? GetAccountOverride(string optionName, string accountName)
    {
        string overrides = Bot.Config!.Get<string>(optionName) ?? "";
        foreach (string entry in overrides.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] pair = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && pair[0].Equals(accountName, StringComparison.OrdinalIgnoreCase))
                return pair[1];
        }
        return null;
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

    private static string SafeAccountSlug(string accountName) =>
        new string(accountName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');

    private bool InLeaderPosition() =>
        _tcp != null && _tcp.Connected && !_isParked && !_roomFull
        && IsLeaderInSameCell()
        && string.Equals(_tcpRoom, Bot.Map?.RoomID.ToString(), StringComparison.Ordinal);

    private string? BossInCurrentMap() => _tcpMap.ToLowerInvariant() switch
    {
        "deleuzetundra" when _hdbon => "Blighted Zubami",
        "voidsalek" when _hdbon => "Salek Sprayer",
        "voidnerfkitten" when _hdbon => "Sarah the NerfKitten",
        _ => null,
    };

    private void PublishCurrentPartyStatus()
    {
        bool atPosition = _partyPrepared && InLeaderPosition();
        string? boss = atPosition ? BossInCurrentMap() : null;
        if (boss == null)
            _visibleBoss = "";
        else if (!string.Equals(_visibleBoss, boss, StringComparison.OrdinalIgnoreCase)
            && DateTime.UtcNow >= _nextTargetCheck)
        {
            _nextTargetCheck = DateTime.UtcNow.AddSeconds(5);
            var visible = Bot.Flash.Call("availableMonsters");
            if (visible?.Contains(boss, StringComparison.OrdinalIgnoreCase) == true)
                _visibleBoss = boss;
            else
            {
                Core.Logger($"[Butler] {boss} is missing from the local cell; reloading {_tcpCell}.");
                Bot.Map.Jump(_tcpCell, _tcpPad, autoCorrect: false);
            }
        }

        string state = atPosition && (boss == null || _visibleBoss == boss) ? "READY" : "WAIT";
        PublishPartyStatus(state, Bot.Map?.Name ?? "", (Bot.Map?.RoomID ?? 0).ToString());
    }

    private void PublishPartyStatus(string state, string map, string room)
    {
        if (string.IsNullOrWhiteSpace(_partyStatusFile))
            return;

        string status = $"{state}|{map}|{room}|{Bot.Player?.Cell}";
        if (status == _lastPartyStatus && DateTime.UtcNow - _lastPartyWrite < TimeSpan.FromSeconds(2))
            return;

        try
        {
            File.WriteAllText(_partyStatusFile, status);
            _lastPartyStatus = status;
            _lastPartyWrite = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Core.Logger($"[Butler] Could not publish HDBoN party status: {ex.Message}");
        }
    }

    private void TryGotoLeader()
    {
        if (_gotoPending)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastGotoTime).TotalMilliseconds < GotoMinIntervalMs)
            return;

        // If we haven't received any TCP data yet, wait — don't attempt to follow
        if (string.IsNullOrEmpty(_tcpMap))
            return;

        // If we have TCP data and everything matches, skip the Goto
        if (!string.IsNullOrEmpty(_tcpCell) &&
            string.Equals(_tcpMap, Bot.Map?.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_tcpRoom, Bot.Map?.RoomID.ToString(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_tcpCell, Bot.Player?.Cell, StringComparison.OrdinalIgnoreCase))
            return;

        // Otherwise map/cell/pad differs → Goto to leader
        _lastGotoTime = now;
        _gotoPending = true;

        _ = Task.Run(() =>
        {
            try
            {
                if (Bot.ShouldExit) return;
                if (Bot.Config!.Get<bool>("UseGoto"))
                    Bot.Player?.Goto(playerName);
                else
                {
                    string map = _tcpMap;
                    string room = _tcpRoom;
                    string cell = _tcpCell;
                    string pad = _tcpPad;

                    if (Bot.ShouldExit) return;
                    JoinLeaderMap(map, room);
                    if (Bot.ShouldExit) return;
                    Core.Jump(cell, pad);
                }
            }
            catch { }
            _gotoPending = false;
        });
    }

    // ================================================================
    //  JOIN LEADER MAP (with room validation)
    // ================================================================

    /// <summary>
    /// Joins the leader's map using the TCP-provided map name and room number.
    /// The plugin sends "(no map)" when FullName isn't loaded yet — that's not
    /// an error we need to fix in the plugin (it's already shipped). Instead we
    /// detect those placeholder strings here and skip the join, waiting for the
    /// next broadcast (200ms later) when the leader's UI has caught up.
    /// </summary>
    private void JoinLeaderMap(string map, string room)
    {
        // Validate map — skip if empty or unknown
        if (string.IsNullOrEmpty(map) || map == "unknown")
        {
            Core.Logger($"[Butler] Invalid map data from leader (map='{map}'), skipping join.");
            return;
        }

        // Room empty → leader data isn't ready yet, wait for next broadcast
        if (string.IsNullOrEmpty(room))
        {
            Core.Logger($"[Butler] Leader room not yet available, waiting for next broadcast.");
            return;
        }

        // Plugin sends "(no map)" when FullName UI element hasn't loaded yet.
        // This is normal transient state — skip this tick and wait for valid data.
        if (room.Contains('(') || room.Contains(')'))
        {
            Core.Logger($"[Butler] Leader room is a placeholder ('{room}'), map data not ready yet — waiting.");
            return;
        }

        // Valid numeric room → join with suffix
        if (int.TryParse(room, out _))
        {
            Bot.Map.Join($"{map}-{room}", "Enter", "Spawn", autoCorrect: false);
            return;
        }

        // Non-numeric room with no parentheses → it's the base map name
        // (e.g., the plugin returns "battleon" when there's no room suffix).
        // Join the map without a suffix.
        Core.Logger($"[Butler] Leader room '{room}' is not a number (likely a public/base map), joining directly.");
        Core.Join(map);
    }

    // ================================================================
    //  TCP
    // ================================================================

    private void ConnectToLeader()
    {
        int port = ReadLeaderPort(playerName);
        if (port < 0)
            return;

        try
        {
            _tcp = new TcpClient();
            _tcp.Connect("127.0.0.1", port);
            _tcp.NoDelay = true;
            _stream = _tcp.GetStream();
            _streamReader = new StreamReader(_stream, Encoding.UTF8);

            string handshake = $"HELLO|{Bot.Player.Username}|{playerName}\n";
            byte[] hb = Encoding.UTF8.GetBytes(handshake);
            _stream.Write(hb, 0, hb.Length);
            _stream.Flush();

            _streamReader.ReadLine(); // welcome
        }
        catch
        {
            Disconnect();
        }
    }

    private int ReadLeaderPort(string leaderName)
    {
        try
        {
            var pluginType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LeaderButlerSyncv2.LeaderButlerSyncPlugin", false) ?? assembly.GetType("LeaderButlerSyncPlugin", false))
                .FirstOrDefault(type => type != null);

            var method = pluginType?.GetMethod("ReadLeaderPort", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method?.Invoke(null, new object[] { leaderName }) is int port)
                return port;
        }
        catch (Exception ex)
        {
            if (!_leaderPortLookupFailedLogged)
            {
                Core.Logger($"[Butler] Failed to read leader port from LeaderButlerSyncv2.dll: {ex.Message}");
                _leaderPortLookupFailedLogged = true;
            }
        }

        if (!_leaderPortLookupFailedLogged)
        {
            Core.Logger("[Butler] LeaderButlerSyncv2.dll is not loaded. Put it in Skua/plugins and restart your clients.");
            _leaderPortLookupFailedLogged = true;
        }

        return -1;
    }

    private void PollTcpData()
    {
        if (_tcp == null || !_tcp.Connected)
        {
            ConnectToLeader();
            return;
        }

        try
        {
            while (_stream!.DataAvailable)
            {
                string? line = _streamReader!.ReadLine();
                if (line == null)
                {
                    Disconnect();
                    return;
                }
                var parts = line.Split('|');
                if (parts.Length >= 9)
                {
                    _tcpMap = parts[0] ?? "";
                    _tcpRoom = parts[1] ?? "";
                    _tcpCell = parts[2] ?? "";
                    _tcpPad = parts[3] ?? "";
                    _tcpInCombat = parts[6] == "1";
                    _tcpHasTarget = parts[7] == "1";
                }
            }
        }
        catch
        {
            Disconnect();
        }
    }

    private void Disconnect()
    {
        _streamReader?.Close();
        _stream?.Close();
        _tcp?.Close();
        _streamReader = null;
        _stream = null;
        _tcp = null;
    }

    // ================================================================
    //  COMBAT
    // ================================================================

    private void DontAttack()
    {
        Bot.Combat.CancelTarget();
        Bot.Options.AttackWithoutTarget = false;
        Bot.Options.AggroAllMonsters = false;
        Bot.Options.AggroMonsters = false;
    }

    private bool IsButlerAlive()
    {
        return Bot.Player?.Alive == true;
    }

    private bool IsLeaderInSameCell()
    {
        return !string.IsNullOrEmpty(_tcpCell) &&
               string.Equals(_tcpMap, Bot.Map?.Name, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(_tcpCell, Bot.Player?.Cell, StringComparison.OrdinalIgnoreCase);
    }

    private void QuickDeaggro()
    {
        if (!IsButlerAlive())
            return;

        if (Bot.Player.InCombat || (Bot.Player.HasTarget && (Bot.Player.Target?.HP ?? 0) > 0))
        {
            DontAttack();
            Bot.Map.Jump(Bot.Player.Cell ?? "Enter", Bot.Player.Pad ?? "Left");
        }
    }

    void EnterSafeState(string reason)
    {
        if (_isParked)
            return;

        Core.Logger(reason);

        if (!_houseJoined)
        {
            _houseJoined = true;
            if (Bot.House.Items.Any(h => h.Equipped))
            {
                Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
                Bot.Wait.ForMapLoad("house");
            }
            else Core.Join("yulgar-100000");
        }

        _isParked = true;
    }

    void ChatListener(dynamic packet)
    {
        try
        {
            if (packet == null) return;

            var paramsObj = packet["params"];
            if (paramsObj == null || paramsObj!.type != "str") return;

            dynamic? dataObj = paramsObj!.dataObj;
            if (dataObj == null) return;

            string? cmd = dataObj[0];
            if (string.IsNullOrEmpty(cmd)) return;

            if (cmd == "server")
            {
                string? text = dataObj[2]?.ToString();
                if (!string.IsNullOrEmpty(text) && text.Contains("ignoring goto"))
                    _gotoIgnored = true;
            }
            else if (cmd == "warning")
            {
                string? chat = Convert.ToString(packet);
                if (!string.IsNullOrEmpty(chat))
                {
                    if (chat.Contains("Locked zone") || chat.Contains("not available"))
                        _lockedZone = true;
                    if (chat.Contains("full"))
                        _roomFull = true;
                    if (chat.Contains("PvP zone"))
                        _pvpZone = true;
                    if (chat.Contains("ignoring goto"))
                        _gotoIgnored = true;
                    if (chat.Contains("could not be found"))
                        _differentServer = true;
                }
            }
        }
        catch { }
    }
}
