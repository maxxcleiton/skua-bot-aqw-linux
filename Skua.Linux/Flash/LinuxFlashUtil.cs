using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Utils;

namespace Skua.Linux.Flash;

public class LinuxFlashUtil : IFlashUtil
{
    private readonly IMessenger _messenger;
    private readonly Lazy<IScriptManager> _lazyManager;
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private Process? _flashProcess;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pendingRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _nextReqId;

    public event FlashCallHandler? FlashCall;
    public event Action<int>? FlashProcessStarted;

    public int? FlashProcessId => _flashProcess is { HasExited: false } ? _flashProcess.Id : null;
    public bool IsConnected => _client?.Connected == true;

    public LinuxFlashUtil(IMessenger messenger, Lazy<IScriptManager> manager)
    {
        _messenger = messenger;
        _lazyManager = manager;
    }

    public void InitializeFlash()
    {
        try
        {
            CleanupOldFlash();

            FlashTrustManager.EnsureTrustFile();

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            string baseDir = AppContext.BaseDirectory;
            string portFile = Path.Combine(baseDir, "skua_bridge.port");
            File.WriteAllText(portFile, port.ToString());

            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

            string swfPath = Path.Combine(baseDir, "skua.swf");
            if (!File.Exists(swfPath))
            {
                string repoSwf = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Skua.AS3", "skua", "bin", "skua.swf"));
                if (File.Exists(repoSwf))
                {
                    File.Copy(repoSwf, swfPath, true);
                }
            }

            string? playerBin = ResolveFlashPlayerBinary();
            if (playerBin is null)
            {
                Trace.WriteLine("[LinuxFlashUtil] flashplayer binary not found.");
                return;
            }

            ProcessStartInfo psi = new()
            {
                FileName = playerBin,
                Arguments = $"\"{swfPath}\"",
                WorkingDirectory = baseDir,
                UseShellExecute = false
            };

            _flashProcess = Process.Start(psi);
            if (_flashProcess is not null)
            {
                FlashProcessStarted?.Invoke(_flashProcess.Id);
            }
        }
        catch (Exception ex)
        {
            _messenger.Send<FlashErrorMessage>(new(ex, "InitializeFlash"));
        }
    }

    private static string? ResolveFlashPlayerBinary()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "flashplayer"),
            Path.Combine(home, ".local", "bin", "flashplayer"),
            "/home/max/coding/Skua-Linux/.tools/flashplayer/flashplayer",
            "/usr/bin/flashplayer",
            "/usr/local/bin/flashplayer"
        };

        foreach (string c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        return "flashplayer";
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (ct.IsCancellationRequested)
                    break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            _client = client;
            _stream = stream;

            byte[] readBuf = new byte[65536];
            using MemoryStream frameBuffer = new();

            try
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    int read = await stream.ReadAsync(readBuf, 0, readBuf.Length, ct);
                    if (read <= 0)
                        break;

                    for (int i = 0; i < read; i++)
                    {
                        byte b = readBuf[i];
                        if (b == 0)
                        {
                            string frame = Encoding.UTF8.GetString(frameBuffer.GetBuffer(), 0, (int)frameBuffer.Length);
                            frameBuffer.SetLength(0);
                            if (!string.IsNullOrWhiteSpace(frame))
                            {
                                await ProcessFrameAsync(frame, stream, ct);
                            }
                        }
                        else
                        {
                            frameBuffer.WriteByte(b);
                        }
                    }
                }
            }
            catch
            {
                // Socket closed or cancelled
            }
            finally
            {
                foreach (KeyValuePair<int, TaskCompletionSource<string>> kvp in _pendingRequests)
                {
                    kvp.Value.TrySetCanceled();
                }
                _pendingRequests.Clear();
            }
        }
    }

    private async Task ProcessFrameAsync(string frame, NetworkStream stream, CancellationToken ct)
    {
        if (frame.Contains("<policy-file-request/>", StringComparison.Ordinal))
        {
            byte[] policy = Encoding.UTF8.GetBytes("<cross-domain-policy><allow-access-from domain=\"*\" to-ports=\"*\"/></cross-domain-policy>\0");
            await _writeLock.WaitAsync(ct);
            try
            {
                await stream.WriteAsync(policy, 0, policy.Length, ct);
                await stream.FlushAsync(ct);
            }
            finally
            {
                _writeLock.Release();
            }
            return;
        }

        try
        {
            XElement root = XElement.Parse(frame, LoadOptions.PreserveWhitespace);
            string localName = root.Name.LocalName;

            if (localName == "res")
            {
                if (int.TryParse(root.Attribute("id")?.Value, out int reqId) &&
                    _pendingRequests.TryRemove(reqId, out TaskCompletionSource<string>? tcs))
                {
                    XElement? payloadEl = root.Elements().FirstOrDefault();
                    tcs.TrySetResult(payloadEl?.ToString(SaveOptions.DisableFormatting) ?? "<null/>");
                }
                return;
            }

            if (localName == "call" || localName == "invoke")
            {
                XElement invokeEl = localName == "call" ? (root.Element("invoke") ?? root) : root;
                string? function = invokeEl.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(function))
                    return;

                XElement? argsRoot = invokeEl.Element("arguments");
                object[] args = argsRoot is not null
                    ? argsRoot.Elements().Select(FromFlashXml).ToArray()
                    : invokeEl.Elements().Select(FromFlashXml).ToArray();

                Console.WriteLine($"[LinuxFlashUtil] FlashCall <- {function}({string.Join(", ", args.Select(a => a?.ToString() ?? "null"))})");
                _ = Task.Run(() => DispatchFlashCall(function, args));
            }
        }
        catch (Exception ex)
        {
            _messenger.Send<FlashErrorMessage>(new(ex, "ProcessFrameAsync"));
        }
    }

    private void DispatchFlashCall(string function, object[] args)
    {
        FlashCallHandler? subscribers = FlashCall;
        if (subscribers is null)
            return;

        foreach (FlashCallHandler handler in subscribers.GetInvocationList().Cast<FlashCallHandler>())
        {
            try
            {
                handler(function, args);
            }
            catch (Exception ex)
            {
                _messenger.Send<FlashErrorMessage>(new(ex, function, args));
            }
        }
    }

    private void CleanupOldFlash()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        catch { }

        try
        {
            _stream?.Dispose();
            _client?.Dispose();
            _listener?.Stop();
        }
        catch { }

        _stream = null;
        _client = null;
        _listener = null;

        if (_flashProcess is not null)
        {
            try
            {
                if (!_flashProcess.HasExited)
                    _flashProcess.Kill();
            }
            catch { }
            finally
            {
                _flashProcess.Dispose();
                _flashProcess = null;
            }
        }
    }

    public string? Call(string function, params object[] args)
    {
        return Call<string>(function, args);
    }

    public T? Call<T>(string function, params object[] args)
    {
        try
        {
            object? o = Call(function, typeof(T), args);
            return o is not null ? (T)o : (T?)DefaultProvider.GetDefault<T>(typeof(T));
        }
        catch
        {
            return (T?)DefaultProvider.GetDefault<T>(typeof(T));
        }
    }

    public object? Call(string function, Type type, params object[] args)
    {
        if (_lazyManager.Value.ShouldExit && Thread.CurrentThread.Name == "Script Thread")
            _lazyManager.Value.ScriptCts?.Token.ThrowIfCancellationRequested();

        NetworkStream? stream = _stream;
        if (stream is null || _client?.Connected != true)
            return default;

        int reqId = Interlocked.Increment(ref _nextReqId);
        TaskCompletionSource<string> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[reqId] = tcs;

        try
        {
            StringBuilder req = new();
            req.Append($"<req id=\"{reqId}\"><invoke name=\"{SecurityElement.Escape(function)}\" returntype=\"xml\">");
            if (args.Length > 0)
            {
                req.Append("<arguments>");
                foreach (object o in args)
                    req.Append(ToFlashXml(o));
                req.Append("</arguments>");
            }
            req.Append("</invoke></req>\0");

            byte[] payload = Encoding.UTF8.GetBytes(req.ToString());
            _writeLock.Wait();
            try
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }
            finally
            {
                _writeLock.Release();
            }

            if (!tcs.Task.Wait(TimeSpan.FromSeconds(8)))
            {
                _pendingRequests.TryRemove(reqId, out _);
                return default;
            }

            string result = tcs.Task.Result;
            if (string.IsNullOrEmpty(result))
                return default;

            XElement el = XElement.Parse(result, LoadOptions.PreserveWhitespace);
            if (el.Name.LocalName == "null" || el.Name.LocalName == "undefined")
                return default;
            if (el.Name.LocalName == "true")
                return Convert.ChangeType("true", type);
            if (el.Name.LocalName == "false")
                return Convert.ChangeType("false", type);

            return string.IsNullOrEmpty(el.Value) && el.FirstNode is null
                ? default
                : Convert.ChangeType(el.Value, type);
        }
        catch (Exception e)
        {
            _pendingRequests.TryRemove(reqId, out _);
            _messenger.Send<FlashErrorMessage>(new(e, function, args));
            return default;
        }
    }

    public static string ToFlashXml(object? o)
    {
        switch (o)
        {
            case null:
                return "<null/>";

            case bool b:
                return b ? "<true/>" : "<false/>";

            case double:
            case float:
            case long:
            case int:
                return $"<number>{o}</number>";

            case ExpandoObject:
                StringBuilder sb = new StringBuilder().Append("<object>");
                foreach (KeyValuePair<string, object?> kvp in (IDictionary<string, object?>)o)
                    sb.Append($"<property id=\"{SecurityElement.Escape(kvp.Key)}\">{ToFlashXml(kvp.Value)}</property>");
                return sb.Append("</object>").ToString();

            default:
                if (o is Array arr)
                {
                    StringBuilder arrSb = new StringBuilder().Append("<array>");
                    int k = 0;
                    foreach (object? el in arr)
                        arrSb.Append($"<property id=\"{k++}\">{ToFlashXml(el)}</property>");
                    return arrSb.Append("</array>").ToString();
                }
                return $"<string>{SecurityElement.Escape(o.ToString())}</string>";
        }
    }

    public object FromFlashXml(XElement el)
    {
        switch (el.Name.LocalName)
        {
            case "number":
                return int.TryParse(el.Value, out int i) ? i : float.TryParse(el.Value, out float f) ? f : 0;
            case "true":
                return true;
            case "false":
                return false;
            case "null":
                return null!;
            case "array":
                return el.Elements().Select(e => e.Name.LocalName == "property" && e.Elements().Any() ? FromFlashXml(e.Elements().First()) : FromFlashXml(e)).ToArray();
            case "object":
                dynamic d = new ExpandoObject();
                IDictionary<string, object?> dict = (IDictionary<string, object?>)d;
                foreach (XElement prop in el.Elements())
                {
                    string? id = prop.Attribute("id")?.Value;
                    if (id is not null && prop.Elements().Any())
                        dict[id] = FromFlashXml(prop.Elements().First());
                }
                return d;
            default:
                return el.Value;
        }
    }

    public IFlashObject<T> CreateFlashObject<T>(string path)
    {
        return new FlashObject<T>(Call<int>("lnkCreate", path), this);
    }

    public void Dispose()
    {
        CleanupOldFlash();
        GC.SuppressFinalize(this);
    }
}
