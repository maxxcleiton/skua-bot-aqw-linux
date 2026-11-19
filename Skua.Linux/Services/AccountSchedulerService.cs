using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.ViewModels;
using Skua.Core.ViewModels.Manager;

namespace Skua.Linux.Services;

public partial class ScheduledTask : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string _server = "Twilly";

    [ObservableProperty]
    private string _scheduledTime = "04:00";

    [ObservableProperty]
    private string _mode = "Script"; // "Script" or "Queue"

    [ObservableProperty]
    private string _targetPath = string.Empty;

    [ObservableProperty]
    private bool _autoClose = true;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _lastRun = "Nunca";

    public string LastRunDate { get; set; } = string.Empty;
}

public sealed partial class AccountSchedulerService : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ILogService _logger;
    private readonly AccountManagerViewModel _accountManager;
    private readonly string _storagePath;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    public ObservableCollection<ScheduledTask> Tasks { get; } = new();

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private string _statusText = "Monitorando horários";

    public AccountSchedulerService(
        ISettingsService settingsService,
        ILogService logger,
        AccountManagerViewModel accountManager)
    {
        _settingsService = settingsService;
        _logger = logger;
        _accountManager = accountManager;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _storagePath = Path.Combine(home, ".config", "Skua", "scheduler.json");

        LoadTasks();
        StartMonitor();
    }

    public void StartMonitor()
    {
        if (_cts != null)
            return;

        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        IsActive = true;
        StatusText = "Monitorando horários diários";
    }

    public void StopMonitor()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        IsActive = false;
        StatusText = "Agendador pausado";
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(25000, token);

                if (!IsActive || Tasks.Count == 0)
                    continue;

                string currentTime = DateTime.Now.ToString("HH:mm");
                string currentDate = DateTime.Today.ToString("yyyy-MM-dd");

                List<ScheduledTask> toRun = Tasks
                    .Where(t => t.IsEnabled
                                && t.ScheduledTime == currentTime
                                && t.LastRunDate != currentDate)
                    .ToList();

                foreach (ScheduledTask task in toRun)
                {
                    task.LastRunDate = currentDate;
                    task.LastRun = $"{DateTime.Now:yyyy-MM-dd HH:mm}";
                    SaveTasks();

                    _logger.ScriptLog($"[Scheduler] Horário atingido ({currentTime})! Iniciando tarefa para conta '{task.AccountName}'...");
                    LaunchScheduledTask(task);

                    // Delay slightly between multiple launches
                    await Task.Delay(5000, token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.ScriptLog($"[Scheduler Erro] {ex.Message}");
            }
        }
    }

    public void LaunchScheduledTask(ScheduledTask task)
    {
        try
        {
            // Find account credentials in AccountManager
            AccountItemViewModel? account = _accountManager.Accounts.FirstOrDefault(a =>
                string.Equals(a.DisplayName, task.AccountName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.Username, task.AccountName, StringComparison.OrdinalIgnoreCase));

            if (account is null)
            {
                _logger.ScriptLog($"[Scheduler Erro] Conta '{task.AccountName}' não encontrada no Gerenciador de Contas!");
                return;
            }

            string exePath = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Skua.exe" : "Skua");
            if (!File.Exists(exePath))
            {
                string candidate = Path.Combine(AppContext.BaseDirectory, "Skua");
                if (File.Exists(candidate))
                    exePath = candidate;
            }

            ProcessStartInfo psi = new(exePath)
            {
                ArgumentList =
                {
                    "-u",
                    account.Username,
                    "-p",
                    account.Password,
                    "-s",
                    !string.IsNullOrWhiteSpace(task.Server) ? task.Server : "Twilly"
                },
                WorkingDirectory = AppContext.BaseDirectory
            };

            if (string.Equals(task.Mode, "Queue", StringComparison.OrdinalIgnoreCase))
            {
                psi.ArgumentList.Add("--run-queue");
                psi.ArgumentList.Add(task.TargetPath);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(task.TargetPath))
                {
                    psi.ArgumentList.Add("--run-script");
                    psi.ArgumentList.Add(task.TargetPath);
                }
            }

            if (task.AutoClose)
            {
                psi.ArgumentList.Add("--auto-close");
            }

            if (!OperatingSystem.IsWindows())
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                psi.Environment["DOTNET_ROOT"] = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? Path.Combine(home, ".dotnet");
                psi.Environment["GDK_BACKEND"] = "x11";
                psi.Environment["WAYLAND_DISPLAY"] = "";
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                psi.Environment["PATH"] = $"{Path.Combine(home, ".dotnet")}:{Path.Combine(home, ".local", "bin")}:{path}";
            }

            Process? proc = Process.Start(psi);
            if (proc != null)
            {
                _logger.ScriptLog($"[Scheduler] Processo iniciado (PID: {proc.Id}) para '{account.DisplayName}' ({task.Mode}: {task.TargetPath})!");
            }
        }
        catch (Exception ex)
        {
            _logger.ScriptLog($"[Scheduler Erro ao disparar processo] {ex.Message}");
        }
    }

    public void AddTask(ScheduledTask task)
    {
        Tasks.Add(task);
        SaveTasks();
        _logger.ScriptLog($"[Scheduler] Nova tarefa agendada: {task.AccountName} às {task.ScheduledTime} ({task.Mode}: {task.TargetPath})");
    }

    public void RemoveTask(ScheduledTask task)
    {
        Tasks.Remove(task);
        SaveTasks();
    }

    public void SaveTasks()
    {
        try
        {
            string dir = Path.GetDirectoryName(_storagePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(Tasks, Formatting.Indented);
            File.WriteAllText(_storagePath, json);
        }
        catch { }
    }

    public void LoadTasks()
    {
        try
        {
            Tasks.Clear();
            if (File.Exists(_storagePath))
            {
                string json = File.ReadAllText(_storagePath);
                List<ScheduledTask>? loaded = JsonConvert.DeserializeObject<List<ScheduledTask>>(json);
                if (loaded != null)
                {
                    foreach (ScheduledTask t in loaded)
                        Tasks.Add(t);
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        StopMonitor();
    }
}
