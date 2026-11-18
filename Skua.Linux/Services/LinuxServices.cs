using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommandLine;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Services;
using Skua.Core.Utils;
using Skua.Core.ViewModels;
using Skua.Linux.Flash;

namespace Skua.Linux.Services;

public static class ConfigureLinuxServices
{
    public static IServiceCollection AddLinuxServices(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, LinuxSettingsService>();
        services.AddSingleton<IFlashUtil, LinuxFlashUtil>();
        services.AddSingleton<IDispatcherService, LinuxDispatcherService>();
        services.AddSingleton<IClipboardService, LinuxClipboardService>();
        services.AddSingleton<IDialogService, LinuxDialogService>();
        services.AddSingleton<IWindowService, LinuxWindowService>();
        services.AddSingleton<IFileDialogService, LinuxFileDialogService>();
        services.AddSingleton<IHotKeyService, LinuxHotKeyService>();
        services.AddSingleton<IThemeService, LinuxThemeService>();
        services.AddSingleton<ISoundService, LinuxSoundService>();
        services.AddSingleton<BackgroundThemeService>();

        return services;
    }

    public static T RunSyncOnUi<T>(Func<Task<T>> asyncFunc)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.InvokeAsync(asyncFunc).GetAwaiter().GetResult();
        }

        T result = default!;
        Exception? caught = null;
        DispatcherFrame frame = new();
        _ = Task.Run(async () =>
        {
            try
            {
                result = await Dispatcher.UIThread.InvokeAsync(asyncFunc);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
            finally
            {
                frame.Continue = false;
            }
        });
        Dispatcher.UIThread.PushFrame(frame);
        if (caught is not null)
            throw caught;
        return result;
    }

    public static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}

public class LinuxSettingsService : ISettingsService
{
    private readonly UnifiedSettingsService _unifiedService;

    public LinuxSettingsService()
    {
        _unifiedService = new UnifiedSettingsService();
        _unifiedService.Initialize(AppRole.Client);
    }

    public T? Get<T>(string key) => _unifiedService.Get<T>(key);
    public T Get<T>(string key, T defaultValue) => _unifiedService.Get<T>(key, defaultValue);
    public void Set<T>(string key, T value) => _unifiedService.Set(key, value);
    public void Initialize(AppRole role) => _unifiedService.Initialize(role);
    public SharedSettings GetShared() => _unifiedService.GetShared();
    public ClientSettings GetClient() => _unifiedService.GetClient();
    public ManagerSettings GetManager() => _unifiedService.GetManager();
    public void SetApplicationVersion() => _unifiedService.SetApplicationVersion();
}

public class LinuxDispatcherService : IDispatcherService
{
    public void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}

public class LinuxClipboardService : IClipboardService
{
    private string _lastText = string.Empty;
    private readonly Dictionary<string, object> _data = new();

    public void SetText(string text)
    {
        _lastText = text ?? string.Empty;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                Window? win = ConfigureLinuxServices.GetMainWindow();
                if (win?.Clipboard is not null)
                    await win.Clipboard.SetTextAsync(_lastText);
            }
            catch { }
        });
    }

    public void SetData(string format, object data)
    {
        if (!string.IsNullOrEmpty(format))
            _data[format] = data;
    }

    public object GetData(string format)
    {
        return _data.TryGetValue(format, out object? val) ? val : null!;
    }

    public string GetText()
    {
        try
        {
            return ConfigureLinuxServices.RunSyncOnUi(async () =>
            {
                Window? win = ConfigureLinuxServices.GetMainWindow();
                if (win?.Clipboard is not null)
                    return await win.Clipboard.GetTextAsync() ?? _lastText;
                return _lastText;
            });
        }
        catch
        {
            return _lastText;
        }
    }
}

public class LinuxSoundService : ISoundService
{
    public void Beep()
    {
        try { Console.Beep(); } catch { }
    }

    public void Beep(int frequency, int duration)
    {
        try { Console.Beep(); } catch { }
    }
}

public class LinuxThemeService : IThemeService
{
    public event ThemeChangedEventHandler? ThemeChanged;
    public event SchemeChangedEventHandler? SchemeChanged;

    public List<object> Presets { get; } = new();
    public List<object> UserThemes { get; } = new();
    public IEnumerable<object> ColorSelectionValues { get; } = Array.Empty<object>();
    public object ColorSelectionValue { get; set; } = new();
    public IEnumerable<object> ContrastValues { get; } = Array.Empty<object>();
    public object ContrastValue { get; set; } = new();
    public float DesiredContrastRatio { get; set; } = 4.5f;
    public bool IsColorAdjusted { get; set; }
    public bool IsDarkTheme { get; set; } = true;
    public object? SelectedColor { get; set; }
    public ColorScheme ActiveScheme { get; set; }

    public void ApplyBaseTheme(bool isDark)
    {
        IsDarkTheme = isDark;
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current is not null)
            {
                Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
            }
        });
    }

    public void ChangeCustomColor(object? obj) => SchemeChanged?.Invoke(ActiveScheme, obj);
    public void ChangeScheme(ColorScheme scheme) => ActiveScheme = scheme;
    public void SaveTheme(string name) { }
    public void SetCurrentTheme(object? theme) => ThemeChanged?.Invoke(theme);
    public void RemoveTheme(object? theme) { }
}

public class LinuxHotKeyService : IHotKeyService
{
    private readonly Dictionary<string, IRelayCommand> _hotKeys;
    private readonly ISettingsService _settingsService;
    private readonly IDecamelizer _decamelizer;

    public LinuxHotKeyService(Dictionary<string, IRelayCommand> hotKeys, ISettingsService settingsService, IDecamelizer decamelizer)
    {
        _hotKeys = hotKeys;
        _settingsService = settingsService;
        _decamelizer = decamelizer;
    }

    public void Reload()
    {
        // Hotkeys are persisted in settings and invoked via MainWindow key bindings
    }

    public List<T> GetHotKeys<T>() where T : IHotKey, new()
    {
        StringCollection hotkeys = _settingsService.Get<StringCollection>("HotKeys") ?? new StringCollection();
        List<T> parsed = new();
        foreach (string? hk in hotkeys)
        {
            if (string.IsNullOrEmpty(hk))
                continue;
            string[] split = hk.Split('|');
            string gesture = split.Length > 1 ? split[1] : string.Empty;
            parsed.Add(new T { Binding = split[0], Title = _decamelizer.Decamelize(split[0], null), KeyGesture = gesture });
        }
        return parsed;
    }

    public HotKey? ParseToHotKey(string keyGesture)
    {
        if (string.IsNullOrWhiteSpace(keyGesture))
            return null;
        string ksc = keyGesture.ToLowerInvariant();
        return new HotKey(
            keyGesture,
            ksc.Contains("ctrl") || ksc.Contains("ctl"),
            ksc.Contains("alt"),
            ksc.Contains("shift"));
    }
}

public class LinuxDialogService : IDialogService
{
    public bool? ShowDialog<TViewModel>(TViewModel viewModel) where TViewModel : class
    {
        return ShowDialog(viewModel, viewModel.ToString() ?? "Dialog");
    }

    public bool? ShowDialog<TViewModel>(TViewModel viewModel, string title) where TViewModel : class
    {
        if (viewModel is InputDialogViewModel inputVm)
            return ShowInputDialog(inputVm, title);

        if (viewModel is OptionContainerViewModel optVm)
            return ShowOptionContainerDialog(optVm);

        if (viewModel is FastTravelEditorDialogViewModel ftVm)
            return ShowFastTravelEditDialog(ftVm);

        return ShowMessageBox(viewModel.ToString() ?? title, title, true);
    }

    public bool? ShowDialog<TViewModel>(TViewModel viewModel, Action<TViewModel> callback) where TViewModel : class
    {
        bool? res = ShowDialog(viewModel);
        if (res == true)
            callback(viewModel);
        return res;
    }

    private static bool? ShowOptionContainerDialog(OptionContainerViewModel vm)
    {
        try
        {
            return ConfigureLinuxServices.RunSyncOnUi(async () =>
            {
                Window? owner = ConfigureLinuxServices.GetMainWindow();
                if (owner is null || !owner.IsVisible)
                    return (bool?)true;

                bool? dialogResult = false;
                Window win = new()
                {
                    Title = string.IsNullOrEmpty(vm.Title) ? "Script Options" : vm.Title,
                    Width = 560,
                    Height = 520,
                    MinWidth = 420,
                    MinHeight = 320,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = new SolidColorBrush(Color.Parse("#181825"))
                };

                StackPanel itemsPanel = new()
                {
                    Spacing = 10,
                    Margin = new Thickness(4)
                };

                if (!string.IsNullOrWhiteSpace(vm.ResetNotice))
                {
                    itemsPanel.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.Parse("#2A2130")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#F9E2AF")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10),
                        Child = new TextBlock
                        {
                            Text = vm.ResetNotice,
                            Foreground = new SolidColorBrush(Color.Parse("#F9E2AF")),
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 12
                        }
                    });
                }

                List<Action> commitActions = new();

                foreach (IGrouping<string, OptionContainerItemViewModel> group in vm.Options.GroupBy(o => o.Category))
                {
                    if (!string.IsNullOrWhiteSpace(group.Key))
                    {
                        itemsPanel.Children.Add(new TextBlock
                        {
                            Text = group.Key,
                            FontWeight = FontWeight.Bold,
                            FontSize = 13,
                            Foreground = new SolidColorBrush(Color.Parse("#89B4FA")),
                            Margin = new Thickness(2, 6, 0, 2)
                        });
                    }

                    foreach (OptionContainerItemViewModel item in group)
                    {
                        StackPanel row = new() { Spacing = 4 };
                        row.Children.Add(new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(item.Option.DisplayName) ? item.Option.Name : item.Option.DisplayName,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = new SolidColorBrush(Color.Parse("#CDD6F4")),
                            FontSize = 12.5
                        });

                        if (!string.IsNullOrWhiteSpace(item.Option.Description))
                        {
                            row.Children.Add(new TextBlock
                            {
                                Text = item.Option.Description,
                                Foreground = new SolidColorBrush(Color.Parse("#A6ADC8")),
                                TextWrapping = TextWrapping.Wrap,
                                FontSize = 11
                            });
                        }

                        if (item.Type == typeof(bool))
                        {
                            bool initial = item.Value is bool b ? b : string.Equals(item.Value?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                            CheckBox chk = new()
                            {
                                Content = "Enabled",
                                IsChecked = initial,
                                Foreground = new SolidColorBrush(Color.Parse("#CDD6F4"))
                            };
                            commitActions.Add(() => item.Value = chk.IsChecked == true);
                            row.Children.Add(chk);
                        }
                        else if (item.Type.IsEnum && item.EnumValues is not null)
                        {
                            ComboBox cb = new()
                            {
                                ItemsSource = item.EnumValues,
                                SelectedItem = item.SelectedValue ?? item.EnumValues.FirstOrDefault(),
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };
                            commitActions.Add(() => item.SelectedValue = cb.SelectedItem?.ToString() ?? item.SelectedValue);
                            row.Children.Add(cb);
                        }
                        else
                        {
                            TextBox tb = new()
                            {
                                Text = item.Value?.ToString() ?? string.Empty,
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };
                            commitActions.Add(() => item.Value = tb.Text ?? string.Empty);
                            row.Children.Add(tb);
                        }

                        itemsPanel.Children.Add(new Border
                        {
                            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
                            BorderBrush = new SolidColorBrush(Color.Parse("#313244")),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(10, 8),
                            Child = row
                        });
                    }
                }

                Button saveBtn = new()
                {
                    Content = "Save & Continue",
                    Background = new SolidColorBrush(Color.Parse("#A6E3A1")),
                    Foreground = Brushes.Black,
                    FontWeight = FontWeight.Bold,
                    Padding = new Thickness(16, 7)
                };
                Button cancelBtn = new()
                {
                    Content = "Cancel",
                    Padding = new Thickness(14, 7)
                };

                saveBtn.Click += (_, _) =>
                {
                    foreach (Action commit in commitActions)
                        commit();
                    dialogResult = true;
                    win.Close();
                };
                cancelBtn.Click += (_, _) =>
                {
                    dialogResult = false;
                    win.Close();
                };

                Grid root = new()
                {
                    Margin = new Thickness(14),
                    RowDefinitions = new RowDefinitions("Auto,*,Auto")
                };

                TextBlock header = new()
                {
                    Text = "Configure Script Options",
                    FontSize = 15,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#CDD6F4")),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid.SetRow(header, 0);

                ScrollViewer scroll = new() { Content = itemsPanel };
                Grid.SetRow(scroll, 1);

                StackPanel footer = new()
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 12, 0, 0),
                    Children = { cancelBtn, saveBtn }
                };
                Grid.SetRow(footer, 2);

                root.Children.Add(header);
                root.Children.Add(scroll);
                root.Children.Add(footer);
                win.Content = root;

                await win.ShowDialog(owner);
                return dialogResult;
            });
        }
        catch
        {
            return false;
        }
    }

    private static bool? ShowFastTravelEditDialog(FastTravelEditorDialogViewModel vm)
    {
        try
        {
            return ConfigureLinuxServices.RunSyncOnUi(async () =>
            {
                Window? owner = ConfigureLinuxServices.GetMainWindow();
                if (owner is null)
                    return (bool?)false;

                bool? dialogResult = false;
                Window win = new()
                {
                    Title = "Edit Fast Travel",
                    Width = 380,
                    Height = 290,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = new SolidColorBrush(Color.Parse("#181825"))
                };

                TextBox descBox = new() { Text = vm.Editor.Travel.DescriptionName, Watermark = "Name / Description" };
                TextBox mapBox = new() { Text = vm.Editor.Travel.MapName, Watermark = "Map (e.g. battleon)" };
                TextBox cellBox = new() { Text = vm.Editor.Travel.Cell, Watermark = "Cell (e.g. Enter)" };
                TextBox padBox = new() { Text = vm.Editor.Travel.Pad, Watermark = "Pad (e.g. Spawn)" };

                Button saveBtn = new()
                {
                    Content = "Save",
                    Background = new SolidColorBrush(Color.Parse("#89B4FA")),
                    Foreground = Brushes.Black,
                    FontWeight = FontWeight.Bold,
                    Width = 90,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
                Button cancelBtn = new() { Content = "Cancel", Width = 90, HorizontalContentAlignment = HorizontalAlignment.Center };

                saveBtn.Click += (_, _) =>
                {
                    vm.Editor.Travel.DescriptionName = descBox.Text?.Trim() ?? string.Empty;
                    vm.Editor.Travel.MapName = mapBox.Text?.Trim() ?? string.Empty;
                    vm.Editor.Travel.Cell = cellBox.Text?.Trim() ?? "Enter";
                    vm.Editor.Travel.Pad = padBox.Text?.Trim() ?? "Spawn";
                    dialogResult = true;
                    win.Close();
                };
                cancelBtn.Click += (_, _) => { dialogResult = false; win.Close(); };

                win.Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Description", Foreground = new SolidColorBrush(Color.Parse("#A6ADC8")), FontSize = 11 },
                        descBox,
                        new TextBlock { Text = "Map / Cell / Pad", Foreground = new SolidColorBrush(Color.Parse("#A6ADC8")), FontSize = 11 },
                        mapBox,
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
                            Children =
                            {
                                cellBox,
                                padBox
                            }
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Margin = new Thickness(0, 10, 0, 0),
                            Children = { cancelBtn, saveBtn }
                        }
                    }
                };
                Grid.SetColumn(cellBox, 0);
                Grid.SetColumn(padBox, 2);

                await win.ShowDialog(owner);
                return dialogResult;
            });
        }
        catch
        {
            return false;
        }
    }

    private static bool? ShowInputDialog(InputDialogViewModel vm, string title)
    {
        try
        {
            return ConfigureLinuxServices.RunSyncOnUi(async () =>
            {
                Window? owner = ConfigureLinuxServices.GetMainWindow();
                if (owner is null)
                    return (bool?)false;

                bool? dialogResult = false;
                Window win = new()
                {
                    Title = string.IsNullOrEmpty(vm.Title) ? title : vm.Title,
                    Width = 420,
                    Height = 195,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = new SolidColorBrush(Color.Parse("#181825"))
                };

                TextBox inputBox = new()
                {
                    Text = vm.DialogTextInput ?? string.Empty,
                    Margin = new Thickness(0, 8, 0, 14)
                };

                Button okBtn = new()
                {
                    Content = "OK",
                    Width = 90,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Background = new SolidColorBrush(Color.Parse("#89B4FA")),
                    Foreground = Brushes.Black,
                    FontWeight = FontWeight.Bold
                };
                Button cancelBtn = new() { Content = "Cancel", Width = 90, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

                okBtn.Click += (_, _) =>
                {
                    vm.DialogTextInput = inputBox.Text ?? string.Empty;
                    dialogResult = true;
                    win.Close();
                };
                cancelBtn.Click += (_, _) =>
                {
                    dialogResult = false;
                    win.Close();
                };

                win.Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Children =
                    {
                        new TextBlock { Text = vm.DialogHint, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#CDD6F4")) },
                        inputBox,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children = { cancelBtn, okBtn }
                        }
                    }
                };

                await win.ShowDialog(owner);
                return dialogResult;
            });
        }
        catch
        {
            return false;
        }
    }

    public void ShowMessageBox(string message, string caption)
    {
        _ = ShowMessageBox(message, caption, false);
    }

    public bool? ShowMessageBox(string message, string caption, bool yesAndNo)
    {
        DialogResult res = yesAndNo
            ? ShowMessageBox(message, caption, "No", "Yes")
            : ShowMessageBox(message, caption, "OK");
        return yesAndNo ? res.Text == "Yes" : res.Text == "OK";
    }

    public DialogResult ShowMessageBox(string message, string caption, params string[] buttons)
    {
        try
        {
            if (buttons.Length == 0)
                buttons = new[] { "OK" };

            return ConfigureLinuxServices.RunSyncOnUi(async () =>
            {
                Window? owner = ConfigureLinuxServices.GetMainWindow();
                if (owner is null || !owner.IsVisible)
                {
                    Console.WriteLine($"[{caption}] {message}");
                    return new DialogResult(buttons[^1], buttons.Length - 1);
                }

                DialogResult result = DialogResult.Cancelled;
                Window dialog = new()
                {
                    Title = caption,
                    Width = 480,
                    Height = 250,
                    MinWidth = 360,
                    MinHeight = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = new SolidColorBrush(Color.Parse("#181825"))
                };

                StackPanel buttonsPanel = new()
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 12, 0, 0)
                };

                for (int i = 0; i < buttons.Length; i++)
                {
                    int idx = i;
                    string text = buttons[i];
                    bool isPrimary = i == buttons.Length - 1;
                    Button btn = new()
                    {
                        Content = text,
                        MinWidth = 86,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        Padding = new Thickness(12, 6)
                    };
                    if (isPrimary)
                    {
                        btn.Background = new SolidColorBrush(Color.Parse("#89B4FA"));
                        btn.Foreground = Brushes.Black;
                        btn.FontWeight = FontWeight.Bold;
                    }
                    btn.Click += (_, _) =>
                    {
                        result = new DialogResult(text, idx);
                        dialog.Close();
                    };
                    buttonsPanel.Children.Add(btn);
                }

                Grid grid = new()
                {
                    Margin = new Thickness(16),
                    RowDefinitions = new RowDefinitions("*,Auto")
                };

                Border msgCard = new()
                {
                    Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#313244")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12),
                    Child = new ScrollViewer
                    {
                        Content = new SelectableTextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#CDD6F4")),
                            FontSize = 12.5
                        }
                    }
                };
                Grid.SetRow(msgCard, 0);
                Grid.SetRow(buttonsPanel, 1);
                grid.Children.Add(msgCard);
                grid.Children.Add(buttonsPanel);

                dialog.Content = grid;
                await dialog.ShowDialog(owner);
                return result;
            });
        }
        catch
        {
            return DialogResult.Cancelled;
        }
    }
}

public class LinuxFileDialogService : IFileDialogService
{
    public string? OpenFile() => OpenFile(ClientFileSources.SkuaScriptsDIR, "*.*");

    public string? OpenFile(string filters) => OpenFile(ClientFileSources.SkuaScriptsDIR, filters);

    public string? OpenFile(string initialDirectory, string filters)
    {
        try
        {
            return ConfigureLinuxServices.RunSyncOnUi(async () =>
            {
                Window? owner = ConfigureLinuxServices.GetMainWindow();
                if (owner?.StorageProvider is null)
                    return null;

                IStorageFolder? folder = Directory.Exists(initialDirectory)
                    ? await owner.StorageProvider.TryGetFolderFromPathAsync(initialDirectory)
                    : null;

                IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select File",
                    AllowMultiple = false,
                    SuggestedStartLocation = folder
                });

                return files.FirstOrDefault()?.TryGetLocalPath();
            });
        }
        catch
        {
            return null;
        }
    }

    public string? OpenFolder() => OpenFolder(ClientFileSources.SkuaDIR);

    public string? OpenFolder(string initialDirectory)
    {
        try
        {
            return ConfigureLinuxServices.RunSyncOnUi(async () =>
            {
                Window? owner = ConfigureLinuxServices.GetMainWindow();
                if (owner?.StorageProvider is null)
                    return null;

                IStorageFolder? folder = Directory.Exists(initialDirectory)
                    ? await owner.StorageProvider.TryGetFolderFromPathAsync(initialDirectory)
                    : null;

                IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Folder",
                    AllowMultiple = false,
                    SuggestedStartLocation = folder
                });

                return folders.FirstOrDefault()?.TryGetLocalPath();
            });
        }
        catch
        {
            return null;
        }
    }

    public IEnumerable<string>? OpenText()
    {
        string? file = OpenFile();
        return !string.IsNullOrEmpty(file) && File.Exists(file) ? File.ReadLines(file) : null;
    }

    public string? Save() => Save(ClientFileSources.SkuaDIR, "*.*");

    public string? Save(string filters) => Save(ClientFileSources.SkuaDIR, filters);

    public string? Save(string initialDirectory, string filters)
    {
        try
        {
            return ConfigureLinuxServices.RunSyncOnUi(async () =>
            {
                Window? owner = ConfigureLinuxServices.GetMainWindow();
                if (owner?.StorageProvider is null)
                    return null;

                IStorageFolder? folder = Directory.Exists(initialDirectory)
                    ? await owner.StorageProvider.TryGetFolderFromPathAsync(initialDirectory)
                    : null;

                IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save File",
                    SuggestedStartLocation = folder
                });

                return file?.TryGetLocalPath();
            });
        }
        catch
        {
            return null;
        }
    }

    public void SaveText(string contents)
    {
        string? file = Save();
        if (!string.IsNullOrEmpty(file))
            File.WriteAllText(file, contents);
    }

    public void SaveText(IEnumerable<string> contents)
    {
        string? file = Save();
        if (!string.IsNullOrEmpty(file))
            File.WriteAllLines(file, contents);
    }
}

public class LinuxWindowService : IWindowService
{
    private readonly Dictionary<string, IManagedWindow> _managedWindows = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string>? ManagedWindowRequested;

    public void ShowWindow<TViewModel>(int width, int height) where TViewModel : class { }
    public void ShowWindow<TViewModel>() where TViewModel : class { }
    public void ShowWindow<TViewModel>(TViewModel viewModel) where TViewModel : class { }

    public void ShowManagedWindow(string key)
    {
        Dispatcher.UIThread.Post(() => ManagedWindowRequested?.Invoke(key));
    }

    public void RegisterManagedWindow<TViewModel>(string key, TViewModel viewModel) where TViewModel : class, IManagedWindow
    {
        _managedWindows[key] = viewModel;
    }
}

public sealed class SkuaLinuxStartupHandler : IDisposable
{
    private readonly IScriptInterface _bot;
    private readonly ISettingsService _settingsService;
    private readonly ParserResult<SkuaLinuxOptions> _result;
    private bool _disposed;

    public SkuaLinuxStartupHandler(string[] args, IScriptInterface bot, ISettingsService settingsService)
    {
        _bot = bot;
        _bot.Flash.FlashCall += LoadGame;
        _result = Parser.Default.ParseArguments<SkuaLinuxOptions>(args);
        _settingsService = settingsService;
    }

    private void LoadGame(string function, params object[] args)
    {
        if (function == "requestLoadGame")
        {
            _bot.Flash.Call("loadClient");
            _bot.Flash.FlashCall -= LoadGame;
        }
    }

    private void Login(string function, params object[] args)
    {
        if (function == "loaded")
        {
            _bot.Flash.FlashCall -= Login;
            Task.Factory.StartNew(() =>
            {
                _bot.Servers.Relogin(_result.Value.Server);
                if (!string.IsNullOrEmpty(_result.Value.Script))
                    StrongReferenceMessenger.Default.Send<StartScriptMessage, int>(new(_result.Value.Script), (int)MessageChannels.ScriptStatus);
            });
        }
    }

    public bool Execute()
    {
        if (_result.Tag == ParserResultType.NotParsed)
            return false;

        SkuaLinuxOptions options = _result.Value;
        if (!string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password))
        {
            _bot.Flash.FlashCall += Login;
            _bot.Servers.SetLoginInfo(options.Username, options.Password);
        }

        if (!string.IsNullOrEmpty(options.GitHubToken))
            _settingsService.Set("UserGitHubToken", options.GitHubToken);

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bot.Flash.FlashCall -= LoadGame;
        _bot.Flash.FlashCall -= Login;
    }
}

public sealed class SkuaLinuxOptions
{
    [Option('u', "user")]
    public string? Username { get; set; }

    [Option('p', "password")]
    public string? Password { get; set; }

    [Option('s', "server", Default = "Twilly")]
    public string Server { get; set; } = "Twilly";

    [Option("run-script")]
    public string? Script { get; set; }

    [Option("use-theme")]
    public string? UseTheme { get; set; }

    [Option("gh-token")]
    public string? GitHubToken { get; set; }
}
