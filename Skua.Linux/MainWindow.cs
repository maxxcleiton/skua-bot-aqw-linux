using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Quests;
using Skua.Core.Models.Shops;
using Skua.Core.ViewModels;
using Skua.Linux.Flash;
using Skua.Linux.Services;

namespace Skua.Linux;

public class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IScriptInterface _bot;
    private readonly LinuxFlashUtil _flashUtil;
    private readonly X11FlashHost _flashHost;
    private readonly TextBlock _flashStatusBadge;
    private readonly TabControl _rightTabs;
    private bool _isDocked = true;

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        _bot = services.GetRequiredService<IScriptInterface>();
        _flashUtil = (LinuxFlashUtil)services.GetRequiredService<IFlashUtil>();

        Title = "Skua Linux (Native .NET 10 + Flash Socket Bridge)";
        Width = 1480;
        Height = 840;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#14141F"));

        _flashHost = new X11FlashHost
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _flashStatusBadge = new TextBlock
        {
            Text = "Starting Flash...",
            Foreground = new SolidColorBrush(Color.Parse("#F9E2AF")),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(8, 0)
        };

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
            ColumnDefinitions = new ColumnDefinitions("*,6,520")
        };

        Border gameBorder = new()
        {
            Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.Parse("#313244")),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 0, 0, 6),
            Child = _flashHost
        };
        Grid.SetColumn(gameBorder, 0);

        GridSplitter splitter = new()
        {
            Background = new SolidColorBrush(Color.Parse("#313244")),
            ResizeDirection = GridResizeDirection.Columns
        };
        Grid.SetColumn(splitter, 1);

        Border toolsBorder = new()
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#313244")),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 6),
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
                _flashStatusBadge.Text = $"Flash PID {pid} (Connecting...)";
            });
        };

        _bot.Flash.FlashCall += OnFlashCall;
        Opened += (_, _) => _bot.Flash.InitializeFlash();
    }

    private void OnFlashCall(string function, params object[] args)
    {
        if (function is "requestLoadGame" or "loaded")
        {
            Dispatcher.UIThread.Post(() =>
            {
                _flashStatusBadge.Text = function == "loaded" ? "AQW Loaded (Connected)" : "Loading AQW Client...";
                _flashStatusBadge.Foreground = new SolidColorBrush(Color.Parse(function == "loaded" ? "#A6E3A1" : "#89B4FA"));
                if (_isDocked && _flashUtil.FlashProcessId is int pid)
                {
                    _flashHost.AttachFlashProcess(pid);
                }
            });
        }
    }

    private void OnManagedWindowRequested(string key)
    {
        int idx = key.ToLowerInvariant() switch
        {
            "scripts" => 0,
            "script repo" => 1,
            "grabber" => 2,
            "logger" or "spammer" or "interceptor" => 3,
            "loader" or "jump" or "auto" or "runtime" or "fast travel" => 4,
            "game" or "application" or "corebots" => 5,
            "console" or "logs" or "stats" => 6,
            _ => -1
        };
        if (idx >= 0 && idx < _rightTabs.ItemCount)
            _rightTabs.SelectedIndex = idx;
    }

    private Border BuildTopToolbar()
    {
        ScriptLoaderViewModel scriptVm = _services.GetRequiredService<ScriptLoaderViewModel>();

        TextBlock scriptNameText = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(8, 0)
        };
        scriptNameText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptLoaderViewModel.LoadedScript)) { Source = scriptVm, FallbackValue = "No Script" });

        TextBlock scriptStatusText = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#CBA6F7")),
            Margin = new Thickness(4, 0, 12, 0)
        };
        scriptStatusText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptLoaderViewModel.ScriptStatus)) { Source = scriptVm });

        Button loadScriptBtn = new() { Content = "Load Script", Command = scriptVm.LoadScriptCommand, Margin = new Thickness(2) };
        Button toggleScriptBtn = new()
        {
            Content = "Start / Stop Script",
            Command = scriptVm.ToggleScriptCommand,
            Background = new SolidColorBrush(Color.Parse("#89B4FA")),
            Foreground = Brushes.Black,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(2)
        };
        Button bankBtn = new()
        {
            Content = "Open Bank",
            Margin = new Thickness(2)
        };
        bankBtn.Click += (_, _) => Task.Run(() => _bot.Bank.Open());

        Button reloadFlashBtn = new()
        {
            Content = "Reload Flash",
            Margin = new Thickness(2)
        };
        reloadFlashBtn.Click += (_, _) => _bot.Flash.InitializeFlash();

        Button dockToggleBtn = new()
        {
            Content = "Undock / Dock Game",
            Margin = new Thickness(2)
        };
        dockToggleBtn.Click += (_, _) =>
        {
            if (_isDocked)
            {
                _flashHost.UndockFlashWindow();
                _isDocked = false;
            }
            else
            {
                _flashHost.RedockFlashWindow();
                _isDocked = true;
            }
        };

        ComboBox fpsSelector = new()
        {
            ItemsSource = new[] { "60 FPS", "120 FPS", "240 FPS", "30 FPS" },
            SelectedIndex = 0,
            Width = 95,
            Margin = new Thickness(4, 2, 2, 2)
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
            Width = 140,
            Margin = new Thickness(2)
        };
        qualitySelector.SelectionChanged += (_, _) =>
        {
            if (qualitySelector.SelectedItem is string s)
            {
                string q = s.Split(':')[1].Trim();
                Task.Run(() => _bot.Flash.Call("setStageQuality", q));
            }
        };

        CheckBox showFpsChk = new()
        {
            Content = "Show FPS",
            Margin = new Thickness(6, 0)
        };
        showFpsChk.IsCheckedChanged += (_, _) =>
        {
            bool show = showFpsChk.IsChecked == true;
            Task.Run(() =>
            {
                _bot.Options.ShowFPS = show;
                _bot.Flash.Call("setShowFPS", show);
            });
        };

        CheckBox lagKillerChk = new()
        {
            Content = "Lag Killer",
            Margin = new Thickness(6, 0)
        };
        lagKillerChk.IsCheckedChanged += (_, _) =>
        {
            bool lk = lagKillerChk.IsChecked == true;
            Task.Run(() => _bot.Options.LagKiller = lk);
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#181825")),
            BorderBrush = new SolidColorBrush(Color.Parse("#313244")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Child = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = "SKUA LINUX",
                        FontWeight = FontWeight.ExtraBold,
                        Foreground = new SolidColorBrush(Color.Parse("#89B4FA")),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 8, 0)
                    },
                    _flashStatusBadge,
                    loadScriptBtn,
                    toggleScriptBtn,
                    scriptNameText,
                    scriptStatusText,
                    bankBtn,
                    reloadFlashBtn,
                    dockToggleBtn,
                    fpsSelector,
                    qualitySelector,
                    showFpsChk,
                    lagKillerChk
                }
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
                new() { Header = "Repo", Content = BuildScriptRepoTab() },
                new() { Header = "Grabber", Content = BuildGrabberTab() },
                new() { Header = "Packets", Content = BuildPacketsTab() },
                new() { Header = "Auto/Map", Content = BuildLoaderJumpAutoTab() },
                new() { Header = "Options", Content = BuildOptionsTab() },
                new() { Header = "Dev/Logs", Content = BuildConsoleAndLogsTab() }
            }
        };
    }

    private Control BuildScriptsTab()
    {
        ScriptLoaderViewModel vm = _services.GetRequiredService<ScriptLoaderViewModel>();

        Grid grid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            Margin = new Thickness(8)
        };

        WrapPanel actions = new()
        {
            Children =
            {
                new Button { Content = "Load Script (.cs)", Command = vm.LoadScriptCommand, Margin = new Thickness(3) },
                new Button { Content = "Start / Stop", Command = vm.ToggleScriptCommand, Margin = new Thickness(3), FontWeight = FontWeight.Bold },
                new Button { Content = "Edit Script (VSCode)", Command = vm.EditScriptCommand, Margin = new Thickness(3) },
                new Button { Content = "Script Options", Command = vm.EditScriptConfigCommand, Margin = new Thickness(3) },
                new Button { Content = "Open Scripts Folder", Command = vm.OpenVSCodeCommand, Margin = new Thickness(3) }
            }
        };
        Grid.SetRow(actions, 0);

        StackPanel infoPanel = new()
        {
            Margin = new Thickness(4, 8),
            Spacing = 4
        };
        TextBlock loadedText = new() { Foreground = Brushes.White, FontWeight = FontWeight.SemiBold };
        loadedText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptLoaderViewModel.LoadedScript)) { Source = vm, StringFormat = "Loaded File: {0}" });
        TextBlock statusText = new() { Foreground = new SolidColorBrush(Color.Parse("#A6E3A1")) };
        statusText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptLoaderViewModel.ScriptStatus)) { Source = vm, StringFormat = "Status: {0}" });
        infoPanel.Children.Add(loadedText);
        infoPanel.Children.Add(statusText);
        Grid.SetRow(infoPanel, 1);

        StackPanel logHeader = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(4, 4),
            Children =
            {
                new TextBlock { Text = "Live Script Logs:", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Bold },
                new Button { Content = "Copy", Command = vm.ScriptLogs.CopyLogCommand },
                new Button { Content = "Clear", Command = vm.ScriptLogs.ClearLogCommand },
                new Button { Content = "Save", Command = vm.ScriptLogs.SaveLogCommand }
            }
        };
        Grid.SetRow(logHeader, 2);

        vm.ScriptLogs.IsActive = true;
        ListBox logsList = new()
        {
            ItemsSource = vm.ScriptLogs.Logs,
            Background = new SolidColorBrush(Color.Parse("#11111B")),
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(logsList, 3);

        grid.Children.Add(actions);
        grid.Children.Add(infoPanel);
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
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(8)
        };

        ComboBox filterBox = new()
        {
            ItemsSource = vm.FilterOptions,
            SelectedItem = vm.FilterBy,
            Width = 120,
            Margin = new Thickness(0, 0, 6, 0)
        };
        filterBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(ScriptRepoViewModel.FilterBy)) { Source = vm, Mode = BindingMode.TwoWay });

        TextBox searchBox = new()
        {
            Watermark = "Filter scripts by name or path...",
            Width = 220,
            Margin = new Thickness(0, 0, 6, 0)
        };

        WrapPanel topRow = new()
        {
            Margin = new Thickness(0, 0, 0, 6),
            Children =
            {
                filterBox,
                searchBox,
                new Button { Content = "Refresh Repo", Command = vm.RefreshScriptsCommand, Margin = new Thickness(2) },
                new Button { Content = "Download All", Command = vm.DownloadAllCommand, Margin = new Thickness(2) },
                new Button { Content = "Update Outdated", Command = vm.UpdateAllCommand, Margin = new Thickness(2) }
            }
        };
        Grid.SetRow(topRow, 0);

        TextBlock statusText = new()
        {
            Foreground = new SolidColorBrush(Color.Parse("#F9E2AF")),
            Margin = new Thickness(2, 2, 2, 6)
        };
        statusText.Bind(TextBlock.TextProperty, new Binding(nameof(ScriptRepoViewModel.ProgressReportMessage)) { Source = vm });
        Grid.SetRow(statusText, 1);

        ListBox scriptsList = new()
        {
            ItemsSource = vm.Scripts,
            Background = new SolidColorBrush(Color.Parse("#11111B"))
        };
        scriptsList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(ScriptRepoViewModel.SelectedItem)) { Source = vm, Mode = BindingMode.TwoWay });

        searchBox.TextChanged += (_, _) =>
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
                                (s.Info.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
            }
        };

        Grid.SetRow(scriptsList, 2);

        WrapPanel bottomActions = new()
        {
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                new Button { Content = "Download Selected", Command = vm.DownloadCommand, Margin = new Thickness(2) },
                new Button
                {
                    Content = "Load Selected",
                    Margin = new Thickness(2)
                }.Also(b => b.Click += (_, _) => vm.LoadScriptCommand.Execute(vm.SelectedItem)),
                new Button
                {
                    Content = "Start Selected",
                    Margin = new Thickness(2),
                    FontWeight = FontWeight.Bold
                }.Also(b => b.Click += (_, _) => vm.StartScriptCommand.Execute(vm.SelectedItem)),
                new Button { Content = "Edit in VSCode", Command = vm.OpenScriptCommand, Margin = new Thickness(2) }
            }
        };
        Grid.SetRow(bottomActions, 3);

        grid.Children.Add(topRow);
        grid.Children.Add(statusText);
        grid.Children.Add(scriptsList);
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
            Width = 200,
            Margin = new Thickness(0, 0, 6, 0)
        };
        categoryBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<GrabberListViewModel>((tab, _) =>
            new TextBlock { Text = tab?.Title ?? string.Empty });

        Button grabBtn = new()
        {
            Content = "GRAB NOW",
            Background = new SolidColorBrush(Color.Parse("#A6E3A1")),
            Foreground = Brushes.Black,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 6, 0)
        };

        TextBox filterBox = new()
        {
            Watermark = "Search grabbed items / monsters / quests...",
            Width = 220
        };

        WrapPanel topPanel = new()
        {
            Margin = new Thickness(0, 0, 0, 6),
            Children = { categoryBox, grabBtn, filterBox }
        };
        Grid.SetRow(topPanel, 0);

        TextBlock statusBlock = new()
        {
            Foreground = new SolidColorBrush(Color.Parse("#F9E2AF")),
            Margin = new Thickness(2, 0, 2, 4)
        };
        Grid.SetRow(statusBlock, 1);

        ListBox grabbedList = new()
        {
            Background = new SolidColorBrush(Color.Parse("#11111B")),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((item, _) =>
                new TextBlock
                {
                    Text = FormatGrabbedItem(item),
                    TextWrapping = TextWrapping.Wrap,
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
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                new Button
                {
                    Content = "Copy ID",
                    Margin = new Thickness(2)
                }.Also(b => b.Click += (_, _) =>
                {
                    string id = ExtractItemId(grabbedList.SelectedItem);
                    if (!string.IsNullOrEmpty(id))
                    {
                        clipboard.SetText(id);
                        statusBlock.Text = $"Copied ID: {id}";
                    }
                }),
                new Button
                {
                    Content = "Copy Name",
                    Margin = new Thickness(2)
                }.Also(b => b.Click += (_, _) =>
                {
                    string name = ExtractItemName(grabbedList.SelectedItem);
                    if (!string.IsNullOrEmpty(name))
                    {
                        clipboard.SetText(name);
                        statusBlock.Text = $"Copied Name: {name}";
                    }
                }),
                new Button
                {
                    Content = "Copy C# Bot Snippet",
                    Background = new SolidColorBrush(Color.Parse("#CBA6F7")),
                    Foreground = Brushes.Black,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(2)
                }.Also(b => b.Click += (_, _) =>
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
                Button taskBtn = new()
                {
                    Content = taskVm.Content,
                    Margin = new Thickness(2)
                };
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
            RowDefinitions = new RowDefinitions("Auto,*,Auto,160"),
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
                new Button { Content = "Clear Logs", Command = loggerVm.ClearPacketLogsCommand, Margin = new Thickness(2) },
                new Button { Content = "Save Logs", Command = loggerVm.SavePacketLogsCommand, Margin = new Thickness(2) }
            }
        };
        Grid.SetRow(loggerTop, 0);

        ListBox packetLogsList = new()
        {
            ItemsSource = loggerVm.PacketLogs,
            Background = new SolidColorBrush(Color.Parse("#11111B"))
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
            Margin = new Thickness(0, 6, 0, 4)
        };
        packetInput.Bind(TextBox.TextProperty, new Binding(nameof(PacketSpammerViewModel.PacketText)) { Source = spammerVm, Mode = BindingMode.TwoWay });

        CheckBox clientChk = new() { Content = "Send to Client", Margin = new Thickness(4, 0) };
        clientChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(PacketSpammerViewModel.SendToClient)) { Source = spammerVm, Mode = BindingMode.TwoWay });

        WrapPanel spamControls = new()
        {
            Children =
            {
                new Button { Content = "Send Once", Command = spammerVm.SendPacketCommand, Margin = new Thickness(2) },
                new Button { Content = "Add to Spammer", Command = spammerVm.AddPacketCommand, Margin = new Thickness(2) },
                new Button { Content = "Remove Selected", Command = spammerVm.RemovePacketCommand, Margin = new Thickness(2) },
                new Button { Content = "Start / Stop Spammer", Command = spammerVm.ToggleSpammerCommand, Margin = new Thickness(2), FontWeight = FontWeight.Bold },
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
            Background = new SolidColorBrush(Color.Parse("#11111B")),
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

    private Control BuildLoaderJumpAutoTab()
    {
        LoaderViewModel loaderVm = _services.GetRequiredService<LoaderViewModel>();
        JumpViewModel jumpVm = _services.GetRequiredService<JumpViewModel>();
        AutoViewModel autoVm = _services.GetRequiredService<AutoViewModel>();

        StackPanel panel = new()
        {
            Margin = new Thickness(8),
            Spacing = 12
        };

        // 1. Shop / Quest Loader
        ComboBox loadTypeBox = new()
        {
            ItemsSource = new[] { "Shop ID", "Quest ID(s)" },
            SelectedIndex = 0,
            Width = 120
        };
        loadTypeBox.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(LoaderViewModel.SelectedIndex)) { Source = loaderVm, Mode = BindingMode.TwoWay });

        TextBox loadIdsBox = new()
        {
            Watermark = "Enter Shop ID or comma-separated Quest IDs...",
            Width = 220
        };
        loadIdsBox.Bind(TextBox.TextProperty, new Binding(nameof(LoaderViewModel.InputIDs)) { Source = loaderVm, Mode = BindingMode.TwoWay });

        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#181825")),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Shop & Quest Loader", FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#89B4FA")) },
                    new WrapPanel
                    {
                        Children =
                        {
                            loadTypeBox,
                            loadIdsBox,
                            new Button { Content = "Load Now", Command = loaderVm.LoadCommand, Margin = new Thickness(4, 0) }
                        }
                    }
                }
            }
        });

        // 2. Cell / Pad Jump
        ComboBox cellsBox = new() { ItemsSource = jumpVm.Cells, Width = 140, Margin = new Thickness(0, 0, 6, 0) };
        cellsBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(JumpViewModel.SelectedCell)) { Source = jumpVm, Mode = BindingMode.TwoWay });

        ComboBox padsBox = new() { ItemsSource = jumpVm.Pads, Width = 120, Margin = new Thickness(0, 0, 6, 0) };
        padsBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(JumpViewModel.SelectedPad)) { Source = jumpVm, Mode = BindingMode.TwoWay });

        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#181825")),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Cell & Pad Jump", FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#89B4FA")) },
                    new WrapPanel
                    {
                        Children =
                        {
                            cellsBox,
                            padsBox,
                            new Button { Content = "Refresh Cells", Command = jumpVm.UpdateCellsCommand, Margin = new Thickness(2) },
                            new Button { Content = "Current", Command = jumpVm.GetCurrentCommand, Margin = new Thickness(2) },
                            new Button { Content = "Jump", Command = jumpVm.JumpToCommand, Margin = new Thickness(2), FontWeight = FontWeight.Bold }
                        }
                    }
                }
            }
        });

        // 3. Auto Attack / Auto Hunt
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#181825")),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Auto Attack / Auto Hunt", FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#89B4FA")) },
                    new WrapPanel
                    {
                        Children =
                        {
                            new Button { Content = "Start Auto-Attack", Command = autoVm.StartAutoAttackCommand, Margin = new Thickness(2) },
                            new Button { Content = "Start Auto-Hunt", Command = autoVm.StartAutoHuntCommand, Margin = new Thickness(2) },
                            new Button { Content = "Stop Auto", Command = autoVm.StopAutoAsyncCommand, Margin = new Thickness(2), Background = new SolidColorBrush(Color.Parse("#F38BA8")), Foreground = Brushes.Black }
                        }
                    }
                }
            }
        });

        return new ScrollViewer { Content = panel };
    }

    private Control BuildOptionsTab()
    {
        GameOptionsViewModel gameOpts = _services.GetRequiredService<GameOptionsViewModel>();
        CoreBotsViewModel cboVm = _services.GetRequiredService<CoreBotsViewModel>();

        StackPanel panel = new()
        {
            Margin = new Thickness(8),
            Spacing = 8
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Game & Bot Options",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#89B4FA"))
        });

        foreach (DisplayOptionItemViewModelBase opt in gameOpts.GameOptions)
        {
            if (opt is CommandOptionItemViewModel<bool> boolOpt)
            {
                CheckBox chk = new()
                {
                    Content = boolOpt.Content,
                    IsChecked = boolOpt.Value as bool? ?? false
                };
                chk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(CommandOptionItemViewModel<bool>.Value)) { Source = boolOpt, Mode = BindingMode.TwoWay });
                chk.Click += (_, _) => boolOpt.Command?.Execute(null);
                panel.Children.Add(chk);
            }
        }

        panel.Children.Add(new Separator { Margin = new Thickness(0, 8) });
        panel.Children.Add(new TextBlock
        {
            Text = "CoreBots Profile",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#89B4FA"))
        });
        panel.Children.Add(new WrapPanel
        {
            Children =
            {
                new Button { Content = "Load CoreBots Options", Command = cboVm.LoadCommand, Margin = new Thickness(2) },
                new Button { Content = "Save CoreBots Options", Command = cboVm.SaveCommand, Margin = new Thickness(2) }
            }
        });

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
            RowDefinitions = new RowDefinitions("Auto,180,Auto,Auto,*"),
            Margin = new Thickness(8)
        };

        Grid.SetRow(new TextBlock
        {
            Text = "Interactive C# Bot Console (Bot.*):",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#89B4FA")),
            Margin = new Thickness(0, 0, 0, 4)
        }, 0);

        TextBox codeBox = new()
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"),
            Background = new SolidColorBrush(Color.Parse("#11111B"))
        };
        codeBox.Bind(TextBox.TextProperty, new Binding(nameof(ConsoleViewModel.SnippetText)) { Source = consoleVm, Mode = BindingMode.TwoWay });
        Grid.SetRow(codeBox, 1);

        Button runBtn = new()
        {
            Content = "Run C# Snippet",
            Command = consoleVm.RunCommand,
            Background = new SolidColorBrush(Color.Parse("#A6E3A1")),
            Foreground = Brushes.Black,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 6)
        };
        Grid.SetRow(runBtn, 2);

        WrapPanel debugHeader = new()
        {
            Margin = new Thickness(0, 4),
            Children =
            {
                new TextBlock { Text = "Debug Logs:", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                new Button { Content = "Copy", Command = debugLog.CopyLogCommand, Margin = new Thickness(2) },
                new Button { Content = "Clear", Command = debugLog.ClearLogCommand, Margin = new Thickness(2) }
            }
        };
        Grid.SetRow(debugHeader, 3);

        ListBox debugList = new()
        {
            ItemsSource = debugLog.Logs,
            Background = new SolidColorBrush(Color.Parse("#11111B"))
        };
        Grid.SetRow(debugList, 4);

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
