using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Skua.Core.Interfaces;

namespace LeaderButlerSyncv2;

public class LeaderButlerSyncPlugin : ISkuaPlugin, IDisposable
{
    public string Name => "LeaderButlerSyncv2";
    public string Author => "SkuaLinux";
    public string Description => "Local TCP broadcaster and discovery provider for Butlerv4 synchronization.";
    public List<IOption>? Options => null;

    private static readonly string PortsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Skua",
        "leader_sync_ports.json"
    );

    private static readonly object _fileLock = new();

    private IScriptInterface? _bot;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<TcpClient, StreamWriter> _clients = new();
    private int _assignedPort = -1;
    private string _registeredUsername = "";

    public static int ReadLeaderPort(string leaderName)
    {
        if (string.IsNullOrWhiteSpace(leaderName))
            return -1;

        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(PortsFilePath))
                    return -1;

                string json = File.ReadAllText(PortsFilePath);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                if (dict == null)
                    return -1;

                var match = dict.FirstOrDefault(kvp => kvp.Key.Equals(leaderName.Trim(), StringComparison.OrdinalIgnoreCase));
                return match.Value > 0 ? match.Value : -1;
            }
            catch
            {
                return -1;
            }
        }
    }

    private static void SavePort(string username, int port)
    {
        if (string.IsNullOrWhiteSpace(username) || port <= 0)
            return;

        lock (_fileLock)
        {
            try
            {
                Dictionary<string, int> dict = new(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(PortsFilePath))
                {
                    string json = File.ReadAllText(PortsFilePath);
                    dict = JsonConvert.DeserializeObject<Dictionary<string, int>>(json) 
                           ?? new(StringComparer.OrdinalIgnoreCase);
                }

                dict[username.Trim()] = port;
                string dir = Path.GetDirectoryName(PortsFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(PortsFilePath, JsonConvert.SerializeObject(dict, Formatting.Indented));
            }
            catch { }
        }
    }

    private static void RemovePort(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return;

        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(PortsFilePath))
                    return;

                string json = File.ReadAllText(PortsFilePath);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                if (dict != null && dict.Remove(username.Trim()))
                {
                    File.WriteAllText(PortsFilePath, JsonConvert.SerializeObject(dict, Formatting.Indented));
                }
            }
            catch { }
        }
    }

    public void Load(IServiceProvider provider, IPluginHelper helper)
    {
        _bot = (IScriptInterface)provider.GetService(typeof(IScriptInterface))!;
        StartServer();
    }

    public void StartServer()
    {
        if (_listener != null)
            return;

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _assignedPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            _ = Task.Run(() => BroadcastLoopAsync(_cts.Token));
        }
        catch { }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(token);
                client.NoDelay = true;
                _ = Task.Run(() => HandleClientAsync(client, token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(500, token);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        NetworkStream? stream = null;
        StreamReader? reader = null;
        StreamWriter? writer = null;

        try
        {
            stream = client.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8);
            writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            // Handshake: client sends HELLO|<butler>|<leader>
            string? handshake = await reader.ReadLineAsync(token);
            if (handshake == null || !handshake.StartsWith("HELLO"))
            {
                client.Close();
                return;
            }

            await writer.WriteLineAsync("WELCOME");

            // Send initial location
            string initial = FormatPayload();
            await writer.WriteLineAsync(initial);

            _clients[client] = writer;

            // Keep connection alive while client is connected
            char[] buffer = new char[64];
            while (!token.IsCancellationRequested && client.Connected)
            {
                int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
            }
        }
        catch { }
        finally
        {
            _clients.TryRemove(client, out _);
            try { writer?.Dispose(); } catch { }
            try { reader?.Dispose(); } catch { }
            try { stream?.Dispose(); } catch { }
            try { client.Dispose(); } catch { }
        }
    }

    private string FormatPayload()
    {
        if (_bot == null || !_bot.Player.LoggedIn)
            return "unknown|0|Enter|Spawn|0|0|0|0|0";

        string map = _bot.Map?.Name ?? "unknown";
        string room = (_bot.Map?.RoomID ?? 0).ToString();
        string cell = _bot.Player?.Cell ?? "Enter";
        string pad = _bot.Player?.Pad ?? "Spawn";
        string inCombat = (_bot.Player?.InCombat == true) ? "1" : "0";
        string hasTarget = (_bot.Player?.HasTarget == true) ? "1" : "0";

        return $"{map}|{room}|{cell}|{pad}|0|0|{inCombat}|{hasTarget}|0";
    }

    private async Task BroadcastLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(200, token);

                if (_bot != null && _bot.Player.LoggedIn)
                {
                    string username = _bot.Player.Username;
                    if (!string.IsNullOrEmpty(username) && username != _registeredUsername)
                    {
                        _registeredUsername = username;
                        SavePort(_registeredUsername, _assignedPort);
                    }

                    string payload = FormatPayload();

                    // Broadcast to all connected butlers
                    if (_clients.Count > 0)
                    {
                        foreach (var kvp in _clients)
                        {
                            try
                            {
                                await kvp.Value.WriteLineAsync(payload);
                            }
                            catch
                            {
                                _clients.TryRemove(kvp.Key, out _);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    public void Unload()
    {
        Dispose();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (!string.IsNullOrEmpty(_registeredUsername))
        {
            RemovePort(_registeredUsername);
        }

        foreach (var kvp in _clients)
        {
            try { kvp.Key.Dispose(); } catch { }
        }
        _clients.Clear();

        try { _listener?.Stop(); } catch { }
        _listener = null;
    }
}
