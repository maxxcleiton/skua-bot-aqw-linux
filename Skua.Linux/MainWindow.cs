using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.Items;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Quests;
using Skua.Core.Models.Shops;
using Skua.Core.ViewModels;
using Skua.Linux.Flash;
using Skua.Linux.Services;
using Skua.Linux.UI;

namespace Skua.Linux;

public class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IScriptInterface _bot;
    private readonly LinuxFlashUtil _flashUtil;
    private readonly X11FlashHost _flashHost;
    private readonly Ellipse _flashStatusDot;
    private readonly TextBlock _flashStatusBadge;
    private readonly Border _playerNamePill;
    private readonly TextBlock _playerNameBadge;
    private readonly TabControl _rightTabs;
    private bool _isDocked = true;
    private bool _fpsBoostEnabled = true;

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        _bot = services.GetRequiredService<IScriptInterface>();
        _flashUtil = (LinuxFlashUtil)services.GetRequiredService<IFlashUtil>();

        Title = "Skua Linux — Native .NET 10";
        Width = 1480;
        Height = 840;
        MinWidth = 1080;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UiTheme.BgCrust;
        LoadWindowIcon();

        _flashHost = new X11FlashHost
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _flashStatusDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = UiTheme.Yellow,
            VerticalAlignment = VerticalAlignment.Center
        };

        _flashStatusBadge = new TextBlock
        {
            Text = "Starting Flash...",
            Foreground = UiTheme.Yellow,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            FontSize = 11.5
        };

        _playerNameBadge = new TextBlock
        {
            Text = string.Empty,
            Foreground = UiTheme.Peach,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.Bold,
            FontSize = 11.5
        };
        _playerNamePill = new Border
        {
            IsVisible = false,
            Margin = new Thickness(4, 0, 4, 0),
            Child = _playerNameBadge
        };
        _playerNamePill.Classes.Add("pill");

        _rightTabs = BuildRightTabs();

        Grid rootGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        Border topBar = BuildTopToolbar();
        Grid.SetRow(topBar, 0);
        rootGrid.Children.Add(topBar);

        Grid mainSplit = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,6,540")
        };

        Border gameBorder = new()
        {
            Background = Brushes.Black,
            BorderBrush = UiTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Margin = new Thickness(6, 0, 0, 6),
            Child = _flashHost
        };
        Grid.SetColumn(gameBorder, 0);

        GridSplitter splitter = new()
        {
            Background = Brushes.Transparent,
            ResizeDirection = GridResizeDirection.Columns
        };
        Grid.SetColumn(splitter, 1);

        Border toolsBorder = new()
        {
            Background = UiTheme.BgBase,
            BorderBrush = UiTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(2),
            Child = _rightTabs
        };
        Grid.SetColumn(toolsBorder, 2);

        mainSplit.Children.Add(gameBorder);
        mainSplit.Children.Add(splitter);
        mainSplit.Children.Add(toolsBorder);

        Grid.SetRow(mainSplit, 1);
        rootGrid.Children.Add(mainSplit);

        Content = rootGrid;

        if (services.GetRequiredService<IWindowService>() is LinuxWindowService winSvc)
        {
            winSvc.ManagedWindowRequested += OnManagedWindowRequested;
        }

        _flashUtil.FlashProcessStarted += pid =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _flashStatusDot.Fill = UiTheme.Yellow;
                _flashStatusBadge.Text = $"PID {pid} (Connecting...)";
                _flashStatusBadge.Foreground = UiTheme.Yellow;
                if (_isDocked)
                {
                    _flashHost.AttachFlashProcess(pid);
                }
            });
        };

        StrongReferenceMessenger.Default.Register<MainWindow, LoginMessage, int>(
            this,
            (int)MessageChannels.GameEvents,
            static (win, msg) => Dispatcher.UIThread.Post(() => win.SetLoggedPlayerName(msg.Username)));

        StrongReferenceMessenger.Default.Register<MainWindow, LogoutMessage, int>(
            this,
            (int)MessageChannels.GameEvents,
            static (win, _) => Dispatcher.UIThread.Post(() => win.SetLoggedPlayerName(string.Empty)));

        _bot.Flash.FlashCall += OnFlashCall;
        AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel);
        Activated += (_, _) => _flashHost.FocusFlashWindow();

        Opened += (_, _) => _bot.Flash.InitializeFlash();
        Closed += (_, _) =>
        {
            StrongReferenceMessenger.Default.UnregisterAll(this);
            _flashUtil.Dispose();
        };
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();
        if (focused is TextBox or AutoCompleteBox)
            return;

        _flashHost.FocusFlashWindow();
        if (_flashHost.ForwardKey(e.Key, e.KeyModifiers, isPress: true))
        {
            e.Handled = true;
        }
    }

    private void OnGlobalKeyUp(object? sender, KeyEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();
        if (focused is TextBox or AutoCompleteBox)
            return;

        if (_flashHost.ForwardKey(e.Key, e.KeyModifiers, isPress: false))
        {
            e.Handled = true;
        }
    }

    private void LoadWindowIcon()
    {
        try
        {
            string[] candidates =
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SkuaIcon.png"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "SkuaIcon.png"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SkuaIcon.ico"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "icons", "skua.png"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "icons", "hicolor", "256x256", "apps", "skua.png"),
                "/home/max/coding/Skua-Linux/Assets/SkuaIcon.png"
            };

            foreach (string path in candidates)
            {
                if (System.IO.File.Exists(path))
                {
                    Icon = new WindowIcon(path);
                    break;
                }
            }
        }
        catch { }
    }

    private void SetLoggedPlayerName(string? username)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            _playerNameBadge.Text = username;
            _playerNamePill.IsVisible = true;
            Title = $"Skua Linux — {username}";
        }
        else
        {
            _playerNameBadge.Text = string.Empty;
            _playerNamePill.IsVisible = false;
            Title = "Skua Linux — Native .NET 10";
        }
    }

    private void OnFlashCall(string function, params object[] args)
    {
        if (function is "requestLoadGame" or "loaded")
        {
            Dispatcher.UIThread.Post(() =>
            {
                bool loaded = function == "loaded";
                _flashStatusDot.Fill = loaded ? UiTheme.Green : UiTheme.Blue;
                _flashStatusBadge.Text = loaded ? $"AQW Connected • {_flashUtil.ActiveEngineShort}" : "Loading AQW...";
                _flashStatusBadge.Foreground = loaded ? UiTheme.Green : UiTheme.Blue;
                if (_isDocked && _flashUtil.FlashProcessId is int pid)
                {
                    _flashHost.AttachFlashProcess(pid);
                }
                if (loaded)
                {
                    bool boost = _fpsBoostEnabled;
                    Task.Run(() => _bot.Flash.Call(boost ? "modEnable" : "modDisable", "FpsBoost"));
                }
            });
        }
    }

    private void OnManagedWindowRequested(string key)
    {
        int idx = key.ToLowerInvariant() switch
        {
            "scripts" or "load script" => 0,
            "queue" or "fila" or "pipeline" => 1,
            "script repo" or "search scripts" => 2,
            "runtime" or "current drops" or "notify drop" => 3,
            "accounts" or "manager" => 4,
            "grabber" => 5,
            "loader" or "jump" or "auto" or "fast travel" => 6,
            "logger" or "spammer" or "interceptor" => 7,
            "game" or "application" or "corebots" => 8,
            "console" or "logs" or "stats" => 9,
            _ => -1
        };
        if (idx >= 0 && idx < _rightTabs.ItemCount)
            _rightTabs.SelectedIndex = idx;
    }

    private Border BuildTopToolbar()
    {
        ScriptLoaderViewModel scriptVm = _services.GetRequiredService<ScriptLoaderViewModel>();

        StackPanel brand = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "SKUA",
                    FontWeight = FontWeight.Black,
                    FontSize = 15,
                    Foreground = UiTheme.Blue,
                    VerticalAlignment = VerticalAlignment.Center
                },
                UiTheme.Badge("LINUX", UiTheme.Mauve, UiTheme.BgSurface)
            }
        };

        Border statusPill = new()
        {
            Margin = new Thickness(0, 0, 6, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { _flashStatusDot, _flashStatusBadge }
            }
        };
        statusPill.Classes.Add("pill");

        Button loadScriptBtn = UiTheme.StyledButton("Load Script", "subtle");
        loadScriptBtn.Command = scriptVm.LoadScriptCommand;

        Button toggleScriptBtn = UiTheme.StyledButton("Start / Stop Script", "primary");
        toggleScriptBtn.Command = scriptVm.ToggleScriptCommand;
        scriptVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScriptLoaderViewModel.ScriptStatus))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    bool running = scriptVm.ScriptStatus.Contains("Running", StringComparison.OrdinalIgnoreCase);
                    toggleScriptBtn.Content = running ? "Stop Script" : "Start Script";
                    toggleScriptBtn.Classes.Set("danger", running);
                    toggleScriptBtn.Classes.Set("primary", !running);
                });
            }
        };

        TextBlock scriptNameText = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiTheme.TextPrimary,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(6, 0, 2, 0)
        };
        scriptNameText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptLoaderViewModel.LoadedScript)) { Source = scriptVm, FallbackValue = "" });

        TextBlock scriptStatusText = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiTheme.Mauve,
            FontSize = 11.5,
            Margin = new Thickness(2, 0, 8, 0)
        };
        scriptStatusText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptLoaderViewModel.ScriptStatus)) { Source = scriptVm });

        Button bankBtn = UiTheme.StyledButton("Open Bank", "subtle");
        bankBtn.Click += (_, _) => Task.Run(() => _bot.Bank.Open());

        Button reloadFlashBtn = UiTheme.StyledButton("Reload Flash", "subtle");
        reloadFlashBtn.Click += (_, _) =>
        {
            SetLoggedPlayerName(string.Empty);
            _bot.Flash.InitializeFlash();
        };

        Button dockToggleBtn = UiTheme.StyledButton("Undock Game", "subtle");
        dockToggleBtn.Click += (_, _) =>
        {
            if (_isDocked)
            {
                _flashHost.UndockFlashWindow();
                _isDocked = false;
                dockToggleBtn.Content = "Dock Game";
            }
            else
            {
                _flashHost.RedockFlashWindow();
                _isDocked = true;
                dockToggleBtn.Content = "Undock Game";
            }
        };

        ComboBox fpsSelector = new()
        {
            ItemsSource = new[] { "60 FPS", "120 FPS", "240 FPS", "30 FPS" },
            SelectedIndex = 0,
            Width = 92,
            Margin = new Thickness(0, 0, 6, 0)
        };
        fpsSelector.SelectionChanged += (_, _) =>
        {
            if (fpsSelector.SelectedItem is string s && int.TryParse(s.Split(' ')[0], out int fps))
            {
                Task.Run(() =>
                {
                    _bot.Options.SetFPS = fps;
                    _bot.Flash.Call("setFrameRate", fps);
                });
            }
        };

        ComboBox qualitySelector = new()
        {
            ItemsSource = new[] { "Quality: LOW", "Quality: MEDIUM", "Quality: HIGH" },
            SelectedIndex = 0,
            Width = 132,
            Margin = new Thickness(0, 0, 10, 0)
        };
        qualitySelector.SelectionChanged += (_, _) =>
        {
            if (qualitySelector.SelectedItem is string s)
            {
                string q = s.Split(':')[1].Trim();
                Task.Run(() => _bot.Flash.Call("setStageQuality", q));
            }
        };

        CheckBox fpsBoostChk = new() { Content = "FPS Boost", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
        fpsBoostChk.IsCheckedChanged += (_, _) =>
        {
            bool boost = fpsBoostChk.IsChecked == true;
            _fpsBoostEnabled = boost;
            Task.Run(() => _bot.Flash.Call(boost ? "modEnable" : "modDisable", "FpsBoost"));
        };

        CheckBox hidePlayersChk = new() { Content = "Hide Players", Margin = new Thickness(0, 0, 10, 0) };
        hidePlayersChk.IsCheckedChanged += (_, _) =>
        {
            bool hp = hidePlayersChk.IsChecked == true;
            Task.Run(() => _bot.Options.HidePlayers = hp);
        };

        CheckBox disableFxChk = new() { Content = "Disable FX", Margin = new Thickness(0, 0, 10, 0) };
        disableFxChk.IsCheckedChanged += (_, _) =>
        {
            bool dfx = disableFxChk.IsChecked == true;
            Task.Run(() => _bot.Options.DisableFX = dfx);
        };

        CheckBox showFpsChk = new() { Content = "Show FPS", Margin = new Thickness(0, 0, 10, 0) };
        showFpsChk.IsCheckedChanged += (_, _) =>
        {
            bool show = showFpsChk.IsChecked == true;
            Task.Run(() =>
            {
                _bot.Options.ShowFPS = show;
                _bot.Flash.Call("setShowFPS", show);
            });
        };

        CheckBox lagKillerChk = new() { Content = "Lag Killer", Margin = new Thickness(0, 0, 6, 0) };
        lagKillerChk.IsCheckedChanged += (_, _) =>
        {
            bool lk = lagKillerChk.IsChecked == true;
            Task.Run(() => _bot.Options.LagKiller = lk);
        };

        WrapPanel row1 = new()
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                brand,
                statusPill,
                _playerNamePill,
                loadScriptBtn,
                toggleScriptBtn,
                scriptNameText,
                scriptStatusText,
                bankBtn,
                reloadFlashBtn,
                dockToggleBtn
            }
        };

        WrapPanel row2 = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "Performance:",
                    Foreground = UiTheme.TextDim,
                    FontSize = 11.5,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 8, 0)
                },
                fpsSelector,
                qualitySelector,
                fpsBoostChk,
                hidePlayersChk,
                disableFxChk,
                showFpsChk,
                lagKillerChk
            }
        };

        return new Border
        {
            Background = UiTheme.BgMantle,
            BorderBrush = UiTheme.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { row1, row2 }
            }
        };
    }

    private TabControl BuildRightTabs()
    {
        return new TabControl
        {
            ItemsSource = new TabItem[]
            {
                new() { Header = "Scripts", Content = BuildScriptsTab() },
                new() { Header = "Queue", Content = ScriptQueueTab.BuildQueueTab(_services) },
                new() { Header = "Repo", Content = BuildScriptRepoTab() },
                new() { Header = "Runtime", Content = RuntimeAndTravelTabs.BuildRuntimeTab(_services) },
                new() { Header = "Accounts", Content = AccountsTab.BuildAccountsTab(_services) },
                new() { Header = "Grab", Content = BuildGrabberTab() },
                new() { Header = "Map", Content = RuntimeAndTravelTabs.BuildAutoAndMapTab(_services) },
                new() { Header = "Pkts", Content = BuildPacketsTab() },
                new() { Header = "Opts", Content = BuildOptionsTab() },
                new() { Header = "Logs", Content = BuildConsoleAndLogsTab() }
            }
        };
    }

    private Control BuildScriptsTab()
    {
        ScriptLoaderViewModel vm = _services.GetRequiredService<ScriptLoaderViewModel>();
        ScriptStatsViewModel statsVm = _services.GetRequiredService<ScriptStatsViewModel>();

        Grid grid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            Margin = new Thickness(8)
        };

        // 1. Active Script Card
        TextBlock loadedText = new()
        {
            Foreground = UiTheme.TextPrimary,
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        loadedText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptLoaderViewModel.LoadedScript)) { Source = vm, FallbackValue = "No script loaded" });

        TextBlock statusText = new()
        {
            Foreground = UiTheme.Green,
            FontWeight = FontWeight.SemiBold,
            FontSize = 11.5
        };
        statusText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptLoaderViewModel.ScriptStatus)) { Source = vm });

        Button loadBtn = UiTheme.StyledButton("Load (.cs)", "subtle");
        loadBtn.Command = vm.LoadScriptCommand;
        Button startStopBtn = UiTheme.StyledButton("Start / Stop", "primary");
        startStopBtn.Command = vm.ToggleScriptCommand;
        Button optsBtn = UiTheme.StyledButton("Script Options", "accent");
        optsBtn.Command = vm.EditScriptConfigCommand;
        Button editBtn = UiTheme.StyledButton("Edit", "subtle");
        editBtn.Command = vm.EditScriptCommand;
        Button folderBtn = UiTheme.StyledButton("Folder", "subtle");
        folderBtn.Command = vm.OpenVSCodeCommand;

        Border scriptCard = UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        loadedText,
                        statusText.Also(t => Grid.SetColumn(t, 1))
                    }
                },
                new WrapPanel { Children = { startStopBtn, loadBtn, optsBtn, editBtn, folderBtn } }
            }
        }, margin: new Thickness(0, 0, 0, 8));
        Grid.SetRow(scriptCard, 0);

        // 2. Live Session Stats Card
        static Border StatTile(string label, IScriptBotStats stats, string propName, IBrush valueColor)
        {
            TextBlock val = new()
            {
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = valueColor,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            val.Bind(TextBlock.TextProperty, new Binding(propName) { Source = stats });

            return new Border
            {
                Background = UiTheme.BgCrust,
                BorderBrush = UiTheme.BorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5),
                MinWidth = 72,
                Margin = new Thickness(0, 0, 5, 4),
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        val,
                        new TextBlock
                        {
                            Text = label,
                            FontSize = 10,
                            Foreground = UiTheme.TextMuted,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };
        }

        Button resetStatsBtn = UiTheme.StyledButton("Reset", "subtle");
        resetStatsBtn.Padding = new Thickness(7, 2);
        resetStatsBtn.FontSize = 11;
        resetStatsBtn.Command = statsVm.ResetStatsCommand;

        Border statsCard = UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        UiTheme.SectionTitle("Session Stats", UiTheme.Teal),
                        resetStatsBtn.Also(b => Grid.SetColumn(b, 1))
                    }
                },
                new WrapPanel
                {
                    Children =
                    {
                        StatTile("Kills", statsVm.ScriptStats, nameof(IScriptBotStats.Kills), UiTheme.Red),
                        StatTile("Drops", statsVm.ScriptStats, nameof(IScriptBotStats.Drops), UiTheme.Green),
                        StatTile("Quests", statsVm.ScriptStats, nameof(IScriptBotStats.QuestsCompleted), UiTheme.Blue),
                        StatTile("Deaths", statsVm.ScriptStats, nameof(IScriptBotStats.Deaths), UiTheme.Peach),
                        StatTile("Relogins", statsVm.ScriptStats, nameof(IScriptBotStats.Relogins), UiTheme.Yellow),
                        StatTile("Inv Free", statsVm.ScriptStats, nameof(IScriptBotStats.InventoryFreeSpace), UiTheme.Mauve)
                    }
                }
            }
        }, padding: new Thickness(10, 8), margin: new Thickness(0, 0, 0, 8));
        Grid.SetRow(statsCard, 1);

        // 3. Log Header
        Grid logHeader = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(2, 0, 2, 4),
            Children =
            {
                new TextBlock
                {
                    Text = "Live Script Logs",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.Bold,
                    Foreground = UiTheme.Blue,
                    FontSize = 12.5
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        UiTheme.StyledButton("Copy", "subtle").Also(b => b.Command = vm.ScriptLogs.CopyLogCommand),
                        UiTheme.StyledButton("Clear", "subtle").Also(b => b.Command = vm.ScriptLogs.ClearLogCommand),
                        UiTheme.StyledButton("Save", "subtle").Also(b => b.Command = vm.ScriptLogs.SaveLogCommand)
                    }
                }.Also(p => Grid.SetColumn(p, 1))
            }
        };
        Grid.SetRow(logHeader, 2);

        vm.ScriptLogs.IsActive = true;
        ListBox logsList = new()
        {
            ItemsSource = vm.ScriptLogs.Logs,
            FontFamily = UiTheme.MonoFont,
            FontSize = 11.5
        };
        Grid.SetRow(logsList, 3);

        grid.Children.Add(scriptCard);
        grid.Children.Add(statsCard);
        grid.Children.Add(logHeader);
        grid.Children.Add(logsList);
        return grid;
    }

    private Control BuildScriptRepoTab()
    {
        ScriptRepoViewModel vm = _services.GetRequiredService<ScriptRepoViewModel>();
        vm.IsActive = true;

        Grid grid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            Margin = new Thickness(8)
        };

        ComboBox filterBox = new()
        {
            ItemsSource = vm.FilterOptions,
            SelectedItem = vm.FilterBy,
            Width = 115,
            Margin = new Thickness(0, 0, 6, 4)
        };
        filterBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(ScriptRepoViewModel.FilterBy)) { Source = vm, Mode = BindingMode.TwoWay });

        TextBox searchBox = new()
        {
            Watermark = "Search scripts by name, path or tag...",
            Width = 225,
            Margin = new Thickness(0, 0, 6, 4)
        };

        Button refreshBtn = UiTheme.StyledButton("Refresh", "subtle");
        refreshBtn.Command = vm.RefreshScriptsCommand;
        Button dlAllBtn = UiTheme.StyledButton("Download All", "subtle");
        dlAllBtn.Command = vm.DownloadAllCommand;
        Button updBtn = UiTheme.StyledButton("Update Outdated", "subtle");
        updBtn.Command = vm.UpdateAllCommand;

        WrapPanel topRow = new()
        {
            Margin = new Thickness(0, 0, 0, 4),
            Children = { filterBox, searchBox, refreshBtn, dlAllBtn, updBtn }
        };
        Grid.SetRow(topRow, 0);

        TextBlock statusText = new()
        {
            Foreground = UiTheme.Yellow,
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(2, 0, 2, 4)
        };
        statusText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptRepoViewModel.ProgressReportMessage)) { Source = vm });

        TextBlock countsText = new()
        {
            Foreground = UiTheme.TextDim,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        void UpdateCountsText()
        {
            countsText.Text = $"Showing {vm.BotScriptQuantity}  •  Local: {vm.DownloadedQuantity}  •  Outdated: {vm.OutdatedQuantity}";
        }
        UpdateCountsText();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ScriptRepoViewModel.BotScriptQuantity) or nameof(ScriptRepoViewModel.DownloadedQuantity) or nameof(ScriptRepoViewModel.OutdatedQuantity))
                Dispatcher.UIThread.Post(UpdateCountsText);
        };

        Grid statusRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(2, 0, 2, 4),
            Children =
            {
                statusText,
                countsText.Also(c => Grid.SetColumn(c, 1))
            }
        };
        Grid.SetRow(statusRow, 1);

        ListBox scriptsList = new()
        {
            ItemsSource = vm.Scripts,
            ItemTemplate = new FuncDataTemplate<ScriptInfoViewModel>((script, _) =>
            {
                if (script is null) return new TextBlock();

                Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

                StackPanel textCol = new() { Spacing = 1 };
                textCol.Children.Add(new TextBlock
                {
                    Text = script.FileName,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = UiTheme.TextPrimary,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                textCol.Children.Add(new TextBlock
                {
                    Text = script.FilePath,
                    Foreground = UiTheme.TextDim,
                    FontSize = 10.5,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                Grid.SetColumn(textCol, 0);

                string badgeLabel = script.Downloaded ? (script.Outdated ? "Update" : "Local") : "Cloud";
                IBrush badgeColor = script.Downloaded ? (script.Outdated ? UiTheme.Yellow : UiTheme.Green) : UiTheme.TextMuted;
                Border badge = UiTheme.Badge(badgeLabel, badgeColor);
                Grid.SetColumn(badge, 1);

                row.Children.Add(textCol);
                row.Children.Add(badge);
                return row;
            })
        };
        scriptsList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(ScriptRepoViewModel.SelectedItem)) { Source = vm, Mode = BindingMode.TwoWay });

        TextBlock descTitle = new() { Text = "Select a script to view details", FontWeight = FontWeight.Bold, Foreground = UiTheme.Blue, FontSize = 12 };
        TextBlock descBody = new() { Text = "Clicking 'Start Selected' will automatically download the script and all required CoreBots includes.", Foreground = UiTheme.TextMuted, TextWrapping = TextWrapping.Wrap, FontSize = 11.5 };

        scriptsList.SelectionChanged += (_, _) =>
        {
            if (scriptsList.SelectedItem is ScriptInfoViewModel sel)
            {
                descTitle.Text = $"{sel.FileName}  ({sel.FilePath})";
                descBody.Text = string.IsNullOrWhiteSpace(sel.Info.Description)
                    ? "No description provided."
                    : sel.Info.Description;
            }
        };

        scriptsList.DoubleTapped += (_, _) =>
        {
            if (scriptsList.SelectedItem is ScriptInfoViewModel sel)
                vm.LoadScriptCommand.Execute(sel);
        };

        void ApplySearchFilter()
        {
            string q = searchBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(q))
            {
                scriptsList.ItemsSource = vm.Scripts;
            }
            else
            {
                scriptsList.ItemsSource = vm.Scripts
                    .Where(s => s.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                s.FilePath.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                s.TagSearchText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                (s.Info.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
            }
        }
        searchBox.TextChanged += (_, _) => ApplySearchFilter();

        Grid.SetRow(scriptsList, 2);

        Border detailsCard = UiTheme.Card(new StackPanel
        {
            Spacing = 3,
            Children = { descTitle, descBody }
        }, padding: new Thickness(10, 7), margin: new Thickness(0, 6, 0, 4));
        Grid.SetRow(detailsCard, 3);

        Button startSelectedBtn = UiTheme.StyledButton("Start Selected", "success");
        startSelectedBtn.Click += async (_, _) =>
        {
            ScriptInfoViewModel? target = (scriptsList.SelectedItem as ScriptInfoViewModel) ?? vm.SelectedItem;
            if (target is null)
            {
                vm.ProgressHandler("Select a script in the list first.");
                return;
            }
            await vm.StartScriptCommand.ExecuteAsync(target);
            _rightTabs.SelectedIndex = 0; // Switch to Scripts tab to show compilation & live logs
        };

        Button loadSelectedBtn = UiTheme.StyledButton("Load Selected", "primary");
        loadSelectedBtn.Click += async (_, _) =>
        {
            ScriptInfoViewModel? target = (scriptsList.SelectedItem as ScriptInfoViewModel) ?? vm.SelectedItem;
            if (target is null)
            {
                vm.ProgressHandler("Select a script in the list first.");
                return;
            }
            await vm.LoadScriptCommand.ExecuteAsync(target);
        };

        Button dlSelectedBtn = UiTheme.StyledButton("Download", "subtle");
        dlSelectedBtn.Command = vm.DownloadCommand;

        Button editSelectedBtn = UiTheme.StyledButton("Edit", "subtle");
        editSelectedBtn.Command = vm.OpenScriptCommand;

        WrapPanel bottomActions = new()
        {
            Margin = new Thickness(0, 2, 0, 0),
            Children = { startSelectedBtn, loadSelectedBtn, dlSelectedBtn, editSelectedBtn }
        };
        Grid.SetRow(bottomActions, 4);

        grid.Children.Add(topRow);
        grid.Children.Add(statusRow);
        grid.Children.Add(scriptsList);
        grid.Children.Add(detailsCard);
        grid.Children.Add(bottomActions);
        return grid;
    }

    private Control BuildGrabberTab()
    {
        GrabberViewModel vm = _services.GetRequiredService<GrabberViewModel>();
        IClipboardService clipboard = _services.GetRequiredService<IClipboardService>();

        Grid grid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            Margin = new Thickness(8)
        };

        ComboBox categoryBox = new()
        {
            ItemsSource = vm.GrabberTabs,
            SelectedItem = vm.SelectedTab,
            Width = 175,
            Margin = new Thickness(0, 0, 6, 4)
        };
        categoryBox.ItemTemplate = new FuncDataTemplate<GrabberListViewModel>((tab, _) =>
            new TextBlock { Text = tab?.Title ?? string.Empty });

        Button grabBtn = UiTheme.StyledButton("GRAB NOW", "success", new Thickness(0, 0, 6, 4));

        TextBox filterBox = new()
        {
            Watermark = "Filter grabbed items...",
            Width = 200,
            Margin = new Thickness(0, 0, 0, 4)
        };

        WrapPanel topPanel = new()
        {
            Margin = new Thickness(0, 0, 0, 4),
            Children = { categoryBox, grabBtn, filterBox }
        };
        Grid.SetRow(topPanel, 0);

        TextBlock statusBlock = new()
        {
            Foreground = UiTheme.Yellow,
            FontSize = 11.5,
            Margin = new Thickness(2, 0, 2, 4)
        };
        Grid.SetRow(statusBlock, 1);

        ListBox grabbedList = new()
        {
            ItemTemplate = new FuncDataTemplate<object>((item, _) =>
                new TextBlock
                {
                    Text = FormatGrabbedItem(item),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11.5,
                    Foreground = UiTheme.TextPrimary,
                    Margin = new Thickness(2)
                })
        };
        Grid.SetRow(grabbedList, 2);

        WrapPanel taskButtonsPanel = new()
        {
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(taskButtonsPanel, 3);

        WrapPanel botCodeGeneratorPanel = new()
        {
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                UiTheme.StyledButton("Copy ID", "subtle").Also(b => b.Click += (_, _) =>
                {
                    string id = ExtractItemId(grabbedList.SelectedItem);
                    if (!string.IsNullOrEmpty(id))
                    {
                        clipboard.SetText(id);
                        statusBlock.Text = $"Copied ID: {id}";
                    }
                }),
                UiTheme.StyledButton("Copy Name", "subtle").Also(b => b.Click += (_, _) =>
                {
                    string name = ExtractItemName(grabbedList.SelectedItem);
                    if (!string.IsNullOrEmpty(name))
                    {
                        clipboard.SetText(name);
                        statusBlock.Text = $"Copied Name: {name}";
                    }
                }),
                UiTheme.StyledButton("Copy C# Bot Snippet", "accent").Also(b => b.Click += (_, _) =>
                {
                    string snippet = GenerateBotSnippet(grabbedList.SelectedItem, _bot.Map.LastMap);
                    if (!string.IsNullOrEmpty(snippet))
                    {
                        clipboard.SetText(snippet);
                        statusBlock.Text = $"Copied C#: {snippet}";
                    }
                })
            }
        };
        Grid.SetRow(botCodeGeneratorPanel, 4);

        void BindCurrentGrabberTab(GrabberListViewModel? tab)
        {
            if (tab is null) return;
            tab.IsActive = true;
            grabbedList.ItemsSource = tab.GrabbedItems;
            taskButtonsPanel.Children.Clear();
            foreach (GrabberTaskViewModel taskVm in tab.GrabberCommands)
            {
                Button taskBtn = UiTheme.StyledButton(taskVm.Content, "subtle");
                taskBtn.Click += (_, _) =>
                {
                    List<object> selected = grabbedList.SelectedItem is not null
                        ? new List<object> { grabbedList.SelectedItem }
                        : new List<object>();
                    taskVm.GrabberTaskCommand.Execute(selected);
                };
                taskButtonsPanel.Children.Add(taskBtn);
            }
        }

        void ApplyGrabFilter()
        {
            if (vm.SelectedTab is not GrabberListViewModel tab)
                return;
            string q = filterBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(q))
            {
                grabbedList.ItemsSource = tab.GrabbedItems;
            }
            else
            {
                grabbedList.ItemsSource = tab.GrabbedItems
                    .Where(i => FormatGrabbedItem(i).Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        categoryBox.SelectionChanged += (_, _) =>
        {
            if (categoryBox.SelectedItem is GrabberListViewModel tab)
            {
                vm.SelectedTab = tab;
                BindCurrentGrabberTab(tab);
            }
        };

        grabBtn.Click += async (_, _) =>
        {
            if (vm.SelectedTab is GrabberListViewModel tab)
            {
                statusBlock.Text = $"Grabbing {tab.Title}...";
                await tab.GrabCommand.ExecuteAsync(null);
                ApplyGrabFilter();
                statusBlock.Text = $"Grabbed {tab.GrabbedItems.Count} item(s) in {tab.Title}.";
            }
        };

        filterBox.TextChanged += (_, _) => ApplyGrabFilter();
        BindCurrentGrabberTab(vm.SelectedTab);

        grid.Children.Add(topPanel);
        grid.Children.Add(statusBlock);
        grid.Children.Add(grabbedList);
        grid.Children.Add(taskButtonsPanel);
        grid.Children.Add(botCodeGeneratorPanel);
        return grid;
    }

    private static string FormatGrabbedItem(object? item)
    {
        return item switch
        {
            Monster m => $"[ID: {m.ID} | MapID: {m.MapID}] {m.Name} (Cell: {m.Cell}, HP: {m.HP}/{m.MaxHP}, Race: {m.Race})",
            MapItem mi => $"[MapItem ID: {mi.ID}] Quest ID: {mi.QuestID} (Map: {mi.MapName} / {mi.MapFilePath})",
            Quest q => $"[Quest ID: {q.ID}] {q.Name} (Once: {q.Once}, Slot: {q.Slot}, Value: {q.Value})\n  Reqs: {string.Join(", ", q.AcceptRequirements?.Select(r => $"{r.Name} x{r.Quantity}") ?? Array.Empty<string>())}",
            ShopItem si => $"[Item ID: {si.ID} | ShopItemID: {si.ShopItemID}] {si.Name} (Cost: {si.Cost} {(si.Coins ? "AC" : "Gold")})",
            ShopInfo s => $"[Shop ID: {s.ID}] {s.Name}",
            InventoryItem inv => $"[ID: {inv.ID}] {inv.Name} x{inv.Quantity}/{inv.MaxStack} ({inv.Category}, Equipped: {inv.Equipped}, AC: {inv.Coins})",
            ItemBase ib => $"[ID: {ib.ID}] {ib.Name} x{ib.Quantity}/{ib.MaxStack} ({ib.Category}, Temp: {ib.Temp})",
            null => string.Empty,
            _ => item.ToString() ?? string.Empty
        };
    }

    private static string ExtractItemId(object? item)
    {
        return item switch
        {
            Monster m => m.ID.ToString(),
            MapItem mi => mi.ID.ToString(),
            Quest q => q.ID.ToString(),
            ShopItem si => si.ID.ToString(),
            ShopInfo s => s.ID.ToString(),
            ItemBase ib => ib.ID.ToString(),
            _ => string.Empty
        };
    }

    private static string ExtractItemName(object? item)
    {
        return item switch
        {
            Monster m => m.Name,
            Quest q => q.Name,
            ShopItem si => si.Name,
            ShopInfo s => s.Name,
            ItemBase ib => ib.Name,
            _ => string.Empty
        };
    }

    private static string GenerateBotSnippet(object? item, string? currentMap)
    {
        string map = string.IsNullOrEmpty(currentMap) ? "mapname" : currentMap.ToLowerInvariant();
        return item switch
        {
            Monster m => $"Core.HuntMonster(\"{map}\", \"{m.Name}\", \"ItemName\", 1, isTemp: true);",
            MapItem mi => $"Core.GetMapItem({mi.ID}, 1, \"{map}\"); // Quest {mi.QuestID}",
            Quest q => $"Core.RegisterQuests({q.ID}); // {q.Name}",
            ShopItem si => $"Core.BuyItem(\"{map}\", 0, {si.ID}, 1, shopItemID: {si.ShopItemID}); // {si.Name}",
            ShopInfo s => $"Bot.Shops.Load({s.ID}); // {s.Name}",
            ItemBase ib => $"Core.AddDrop(\"{ib.Name}\"); // ID: {ib.ID}",
            _ => string.Empty
        };
    }

    private Control BuildPacketsTab()
    {
        PacketLoggerViewModel loggerVm = _services.GetRequiredService<PacketLoggerViewModel>();
        PacketSpammerViewModel spammerVm = _services.GetRequiredService<PacketSpammerViewModel>();
        IClipboardService clipboard = _services.GetRequiredService<IClipboardService>();

        Grid grid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,155"),
            Margin = new Thickness(8)
        };

        CheckBox enableLoggerChk = new()
        {
            Content = "Capture Packets",
            Margin = new Thickness(0, 0, 8, 0)
        };
        enableLoggerChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(PacketLoggerViewModel.IsReceivingPackets)) { Source = loggerVm, Mode = BindingMode.TwoWay });

        WrapPanel loggerTop = new()
        {
            Margin = new Thickness(0, 0, 0, 6),
            Children =
            {
                enableLoggerChk,
                UiTheme.StyledButton("Clear Logs", "subtle").Also(b => b.Command = loggerVm.ClearPacketLogsCommand),
                UiTheme.StyledButton("Save Logs", "subtle").Also(b => b.Command = loggerVm.SavePacketLogsCommand)
            }
        };
        Grid.SetRow(loggerTop, 0);

        ListBox packetLogsList = new()
        {
            ItemsSource = loggerVm.PacketLogs,
            FontFamily = UiTheme.MonoFont,
            FontSize = 11
        };
        packetLogsList.SelectionChanged += (_, _) =>
        {
            if (packetLogsList.SelectedItem is string pkt)
            {
                spammerVm.PacketText = pkt;
                clipboard.SetText(pkt);
            }
        };
        Grid.SetRow(packetLogsList, 1);

        TextBox packetInput = new()
        {
            Watermark = "%xt%zm%... or JSON packet",
            FontFamily = UiTheme.MonoFont,
            Margin = new Thickness(0, 6, 0, 4)
        };
        packetInput.Bind(TextBox.TextProperty, new Binding(nameof(PacketSpammerViewModel.PacketText)) { Source = spammerVm, Mode = BindingMode.TwoWay });

        CheckBox clientChk = new() { Content = "Send to Client", Margin = new Thickness(4, 0) };
        clientChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(PacketSpammerViewModel.SendToClient)) { Source = spammerVm, Mode = BindingMode.TwoWay });

        WrapPanel spamControls = new()
        {
            Children =
            {
                UiTheme.StyledButton("Send Once", "primary").Also(b => b.Command = spammerVm.SendPacketCommand),
                UiTheme.StyledButton("Add", "subtle").Also(b => b.Command = spammerVm.AddPacketCommand),
                UiTheme.StyledButton("Remove", "subtle").Also(b => b.Command = spammerVm.RemovePacketCommand),
                UiTheme.StyledButton("Start / Stop Spammer", "accent").Also(b => b.Command = spammerVm.ToggleSpammerCommand),
                clientChk
            }
        };

        StackPanel spammerHeader = new()
        {
            Children = { packetInput, spamControls }
        };
        Grid.SetRow(spammerHeader, 2);

        ListBox spamQueueList = new()
        {
            ItemsSource = spammerVm.Packets,
            FontFamily = UiTheme.MonoFont,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0)
        };
        spamQueueList.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(PacketSpammerViewModel.SelectedIndex)) { Source = spammerVm, Mode = BindingMode.TwoWay });
        Grid.SetRow(spamQueueList, 3);

        grid.Children.Add(loggerTop);
        grid.Children.Add(packetLogsList);
        grid.Children.Add(spammerHeader);
        grid.Children.Add(spamQueueList);
        return grid;
    }

    private Control BuildOptionsTab()
    {
        GameOptionsViewModel gameOpts = _services.GetRequiredService<GameOptionsViewModel>();
        CoreBotsViewModel cboVm = _services.GetRequiredService<CoreBotsViewModel>();
        gameOpts.IsActive = true;

        StackPanel panel = new()
        {
            Margin = new Thickness(8),
            Spacing = 10
        };

        // 1. Boolean Toggles Card
        WrapPanel boolWrap = new() { Orientation = Orientation.Horizontal };
        StackPanel valueOptionsStack = new() { Spacing = 6 };

        foreach (DisplayOptionItemViewModelBase opt in gameOpts.GameOptions)
        {
            if (opt is CommandOptionItemViewModel cmdOpt)
            {
                if (cmdOpt.DisplayType == typeof(bool))
                {
                    CheckBox chk = new()
                    {
                        Content = cmdOpt.Content,
                        Width = 220,
                        Margin = new Thickness(0, 2, 8, 2)
                    };
                    ToolTip.SetTip(chk, cmdOpt.Description);
                    chk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(DisplayOptionItemViewModelBase.Value)) { Source = cmdOpt, Mode = BindingMode.TwoWay });
                    chk.Click += (_, _) => cmdOpt.Command?.Execute(chk.IsChecked == true);
                    boolWrap.Children.Add(chk);
                }
                else if (cmdOpt.DisplayType == typeof(int) || cmdOpt.DisplayType == typeof(string))
                {
                    Grid row = new() { ColumnDefinitions = new ColumnDefinitions("150,*,Auto") };
                    TextBlock lbl = new()
                    {
                        Text = cmdOpt.Content,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = UiTheme.TextPrimary,
                        FontSize = 12
                    };
                    TextBox tb = new()
                    {
                        Text = cmdOpt.Value?.ToString() ?? string.Empty,
                        Margin = new Thickness(6, 0)
                    };
                    Button applyBtn = UiTheme.StyledButton("Set", "primary", new Thickness(0));
                    applyBtn.Click += (_, _) =>
                    {
                        if (cmdOpt.DisplayType == typeof(int))
                        {
                            if (int.TryParse(tb.Text?.Trim(), out int v))
                            {
                                cmdOpt.Value = v;
                                cmdOpt.Command?.Execute(v);
                            }
                        }
                        else
                        {
                            cmdOpt.Value = tb.Text ?? string.Empty;
                            cmdOpt.Command?.Execute(tb.Text ?? string.Empty);
                        }
                    };
                    Grid.SetColumn(lbl, 0);
                    Grid.SetColumn(tb, 1);
                    Grid.SetColumn(applyBtn, 2);
                    row.Children.Add(lbl);
                    row.Children.Add(tb);
                    row.Children.Add(applyBtn);
                    valueOptionsStack.Children.Add(row);
                }
            }
        }

        // Relogin server selector
        ComboBox serverBox = new()
        {
            ItemsSource = gameOpts.ServersList,
            PlaceholderText = "Relogin Server",
            Width = 180
        };
        serverBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(GameOptionsViewModel.SelectedServer)) { Source = gameOpts, Mode = BindingMode.TwoWay });
        gameOpts.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameOptionsViewModel.ServersList))
                Dispatcher.UIThread.Post(() => serverBox.ItemsSource = gameOpts.ServersList);
        };

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                UiTheme.SectionTitle("Game & Bot Toggles", UiTheme.Blue),
                boolWrap,
                new WrapPanel
                {
                    Margin = new Thickness(0, 4, 0, 0),
                    Children =
                    {
                        new TextBlock { Text = "Relogin Server:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = UiTheme.TextMuted },
                        serverBox,
                        UiTheme.StyledButton("Save", "success", new Thickness(6, 0, 2, 0)).Also(b => b.Command = gameOpts.SaveOptionsCommand),
                        UiTheme.StyledButton("Reset", "subtle").Also(b => b.Command = gameOpts.ResetOptionsCommand),
                        UiTheme.StyledButton("Defaults", "subtle").Also(b => b.Command = gameOpts.ResetDefaultOptionsCommand)
                    }
                }
            }
        }));

        if (valueOptionsStack.Children.Count > 0)
        {
            panel.Children.Add(UiTheme.Card(new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    UiTheme.SectionTitle("Numeric & Custom Options", UiTheme.Teal),
                    valueOptionsStack
                }
            }));
        }

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                UiTheme.SectionTitle("CoreBots Profile", UiTheme.Mauve),
                new WrapPanel
                {
                    Children =
                    {
                        UiTheme.StyledButton("Load CoreBots Options", "primary").Also(b => b.Command = cboVm.LoadCommand),
                        UiTheme.StyledButton("Save CoreBots Options", "success").Also(b => b.Command = cboVm.SaveCommand)
                    }
                }
            }
        }));

        // Flash Engine / Runtime Card
        ISettingsService settingsSvc = _services.GetRequiredService<ISettingsService>();
        string currentEngine = settingsSvc.Get<string>("LinuxFlashEngine", "Ruffle") ?? "Ruffle";
        string currentGfx = settingsSvc.Get<string>("LinuxRuffleGraphics", "vulkan") ?? "vulkan";
        string currentFps = settingsSvc.Get<string>("LinuxRuffleFps", "60") ?? "60";

        ComboBox engineCombo = new()
        {
            ItemsSource = new string[] { "The BattleOn Engine (Ruffle Vulkan - 60 FPS)", "Adobe Flash Player (Legado)" },
            SelectedIndex = currentEngine.Equals("AdobeFlash", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            Width = 340
        };
        engineCombo.SelectionChanged += (_, _) =>
        {
            string chosen = engineCombo.SelectedIndex == 1 ? "AdobeFlash" : "Ruffle";
            settingsSvc.Set("LinuxFlashEngine", chosen);
        };

        ComboBox gfxCombo = new()
        {
            ItemsSource = new string[] { "Vulkan (GPU Acelerada - Recomendado)", "OpenGL", "Padrão do Sistema" },
            SelectedIndex = currentGfx switch
            {
                "gl" => 1,
                "default" => 2,
                _ => 0
            },
            Width = 260
        };
        gfxCombo.SelectionChanged += (_, _) =>
        {
            string chosen = gfxCombo.SelectedIndex switch
            {
                1 => "gl",
                2 => "default",
                _ => "vulkan"
            };
            settingsSvc.Set("LinuxRuffleGraphics", chosen);
        };

        ComboBox fpsCombo = new()
        {
            ItemsSource = new string[] { "60 FPS (Padrão)", "120 FPS", "144 FPS", "Taxa do Monitor (Display Refresh)" },
            SelectedIndex = currentFps switch
            {
                "120" => 1,
                "144" => 2,
                "display" => 3,
                _ => 0
            },
            Width = 240
        };
        fpsCombo.SelectionChanged += (_, _) =>
        {
            string chosen = fpsCombo.SelectedIndex switch
            {
                1 => "120",
                2 => "144",
                3 => "display",
                _ => "60"
            };
            settingsSvc.Set("LinuxRuffleFps", chosen);
        };

        TextBlock activeRuntimeStatus = new()
        {
            Text = $"Motor em execução agora: {_flashUtil.ActiveEngine}",
            Foreground = UiTheme.Teal,
            FontSize = 11.5,
            FontStyle = FontStyle.Italic
        };

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                UiTheme.SectionTitle("⚡ Motor Gráfico Flash & BattleOn Engine", UiTheme.Yellow),
                activeRuntimeStatus,
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new StackPanel
                        {
                            Spacing = 3,
                            Margin = new Thickness(0, 0, 10, 8),
                            Children =
                            {
                                new TextBlock { Text = "Engine Flash:", FontSize = 11, Foreground = UiTheme.TextMuted },
                                engineCombo
                            }
                        },
                        new StackPanel
                        {
                            Spacing = 3,
                            Margin = new Thickness(0, 0, 10, 8),
                            Children =
                            {
                                new TextBlock { Text = "Backend Gráfico (Ruffle):", FontSize = 11, Foreground = UiTheme.TextMuted },
                                gfxCombo
                            }
                        },
                        new StackPanel
                        {
                            Spacing = 3,
                            Margin = new Thickness(0, 0, 10, 8),
                            Children =
                            {
                                new TextBlock { Text = "Limite de FPS:", FontSize = 11, Foreground = UiTheme.TextMuted },
                                fpsCombo
                            }
                        }
                    }
                },
                new TextBlock
                {
                    Text = "ℹ️ Dica: A troca de engine passa a valer na próxima vez que o cliente ou nova conta for aberta.",
                    Foreground = UiTheme.TextMuted,
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0)
                }
            }
        }));

        return new ScrollViewer { Content = panel };
    }

    private Control BuildConsoleAndLogsTab()
    {
        ConsoleViewModel consoleVm = _services.GetRequiredService<ConsoleViewModel>();
        IEnumerable<LogTabViewModel> logTabs = _services.GetRequiredService<IEnumerable<LogTabViewModel>>();
        LogTabViewModel debugLog = logTabs.First();
        debugLog.IsActive = true;

        Grid grid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,175,Auto,Auto,*"),
            Margin = new Thickness(8)
        };

        TextBlock title = UiTheme.SectionTitle("Interactive C# Bot Console (Bot.*)", UiTheme.Blue);
        Grid.SetRow(title, 0);

        TextBox codeBox = new()
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = UiTheme.MonoFont,
            FontSize = 12,
            Watermark = "Bot.Log(Bot.Player.Username);"
        };
        codeBox.Bind(TextBox.TextProperty, new Binding(nameof(ConsoleViewModel.SnippetText)) { Source = consoleVm, Mode = BindingMode.TwoWay });
        Grid.SetRow(codeBox, 1);

        Button runBtn = UiTheme.StyledButton("Run C# Snippet", "success", new Thickness(0, 6));
        runBtn.Command = consoleVm.RunCommand;
        Grid.SetRow(runBtn, 2);

        Grid debugHeader = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 4),
            Children =
            {
                UiTheme.SectionTitle("Debug Logs", UiTheme.Mauve),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        UiTheme.StyledButton("Copy", "subtle").Also(b => b.Command = debugLog.CopyLogCommand),
                        UiTheme.StyledButton("Clear", "subtle").Also(b => b.Command = debugLog.ClearLogCommand)
                    }
                }.Also(p => Grid.SetColumn(p, 1))
            }
        };
        Grid.SetRow(debugHeader, 3);

        ListBox debugList = new()
        {
            ItemsSource = debugLog.Logs,
            FontFamily = UiTheme.MonoFont,
            FontSize = 11
        };
        Grid.SetRow(debugList, 4);

        grid.Children.Add(title);
        grid.Children.Add(codeBox);
        grid.Children.Add(runBtn);
        grid.Children.Add(debugHeader);
        grid.Children.Add(debugList);
        return grid;
    }
}

internal static class ControlExtensions
{
    public static T Also<T>(this T control, Action<T> configure)
    {
        configure(control);
        return control;
    }
}
