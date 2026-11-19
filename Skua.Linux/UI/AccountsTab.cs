using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Servers;
using Skua.Core.Utils;
using Skua.Core.ViewModels;
using Skua.Core.ViewModels.Manager;
using Skua.Linux.Services;

namespace Skua.Linux.UI;

internal static class AccountsTab
{
    public static Control BuildAccountsTab(IServiceProvider services)
    {
        return new TabControl
        {
            ItemsSource = new TabItem[]
            {
                new() { Header = "👥 Contas & Grupos", Content = BuildAccountsView(services) },
                new() { Header = "⏰ Agendador Diário", Content = BuildSchedulerView(services) }
            }
        };
    }

    private static Control BuildAccountsView(IServiceProvider services)
    {
        AccountManagerViewModel vm = services.GetRequiredService<AccountManagerViewModel>();
        vm.IsActive = true;

        Grid mainGrid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(8)
        };

        // -------------------------------------------------------------
        // 1. TOP TOOLBAR & MULTI-LAUNCH CARD
        // -------------------------------------------------------------
        Border headerCard = new()
        {
            Background = UiTheme.BgSurface,
            BorderBrush = UiTheme.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        StackPanel headerStack = new() { Spacing = 8 };

        // Server row
        Grid serverRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        TextBlock serverLabel = new()
        {
            Text = "Server:",
            Foreground = UiTheme.TextDim,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        ComboBox serverCombo = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = vm.ServerList,
            ItemTemplate = new FuncDataTemplate<Server>((s, _) =>
            {
                if (s is null) return new TextBlock();
                string pingStr = s.Ping > 0 && s.Ping < 9000 ? $" ({s.Ping}ms)" : "";
                return new TextBlock
                {
                    Text = $"{s.Name}{pingStr}",
                    FontSize = 12,
                    Foreground = UiTheme.TextPrimary
                };
            })
        };
        serverCombo.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(AccountManagerViewModel.SelectedServer))
        {
            Source = vm,
            Mode = BindingMode.TwoWay
        });

        Grid.SetColumn(serverLabel, 0);
        Grid.SetColumn(serverCombo, 1);
        serverRow.Children.Add(serverLabel);
        serverRow.Children.Add(serverCombo);
        headerStack.Children.Add(serverRow);

        // Script selection row
        Grid scriptRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(0, 2, 0, 0)
        };

        TextBlock scriptLabel = new()
        {
            Text = "Script:",
            Foreground = UiTheme.TextDim,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };

        TextBox scriptBox = new()
        {
            IsReadOnly = true,
            Watermark = "No script selected (optional)...",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        scriptBox.Bind(TextBox.TextProperty, new Binding(nameof(AccountManagerViewModel.ScriptPath))
        {
            Source = vm,
            Mode = BindingMode.OneWay
        });

        Button browseScriptBtn = new()
        {
            Content = "📂 Select Script",
            Background = UiTheme.BgSurface,
            Foreground = UiTheme.Blue,
            FontWeight = FontWeight.Bold,
            FontSize = 11.5,
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 4, 0),
            Command = vm.ChangeScriptPathCommand
        };

        Button clearScriptBtn = new()
        {
            Content = "✕",
            Background = UiTheme.BgBase,
            Foreground = UiTheme.Red,
            FontWeight = FontWeight.Bold,
            FontSize = 11.5,
            Padding = new Thickness(8, 5)
        };
        clearScriptBtn.Click += (_, _) =>
        {
            vm.ScriptPath = string.Empty;
            vm.StartWithScript = false;
        };

        Grid.SetColumn(scriptLabel, 0);
        Grid.SetColumn(scriptBox, 1);
        Grid.SetColumn(browseScriptBtn, 2);
        Grid.SetColumn(clearScriptBtn, 3);
        scriptRow.Children.Add(scriptLabel);
        scriptRow.Children.Add(scriptBox);
        scriptRow.Children.Add(browseScriptBtn);
        scriptRow.Children.Add(clearScriptBtn);
        headerStack.Children.Add(scriptRow);

        // Auto-run checkbox row
        CheckBox autoRunCheck = new()
        {
            Content = "Auto-execute selected script on client launch",
            Foreground = UiTheme.TextPrimary,
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 4)
        };
        autoRunCheck.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(AccountManagerViewModel.StartWithScript))
        {
            Source = vm,
            Mode = BindingMode.TwoWay
        });
        headerStack.Children.Add(autoRunCheck);

        // Action buttons row
        WrapPanel actionsRow = new()
        {
            Orientation = Orientation.Horizontal
        };

        Button startSelectedBtn = new()
        {
            Content = "▶ Launch Selected",
            Background = UiTheme.Green,
            Foreground = UiTheme.BgCrust,
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 0, 6, 4),
            Command = vm.StartAccountsCommand
        };

        Button selectAllBtn = new()
        {
            Content = "Select All",
            Background = UiTheme.BgBase,
            Foreground = UiTheme.TextPrimary,
            FontSize = 11.5,
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 0, 6, 4)
        };
        selectAllBtn.Click += (_, _) =>
        {
            foreach (var a in vm.Accounts) a.UseCheck = true;
        };

        Button deselectAllBtn = new()
        {
            Content = "Deselect",
            Background = UiTheme.BgBase,
            Foreground = UiTheme.TextPrimary,
            FontSize = 11.5,
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 0, 6, 4)
        };
        deselectAllBtn.Click += (_, _) =>
        {
            foreach (var a in vm.Accounts) a.UseCheck = false;
        };

        actionsRow.Children.Add(startSelectedBtn);
        actionsRow.Children.Add(selectAllBtn);
        actionsRow.Children.Add(deselectAllBtn);
        headerStack.Children.Add(actionsRow);

        headerCard.Child = headerStack;
        Grid.SetRow(headerCard, 0);
        mainGrid.Children.Add(headerCard);

        // -------------------------------------------------------------
        // 2. QUICK ADD ACCOUNT EXPANDER
        // -------------------------------------------------------------
        Expander addExpander = new()
        {
            Header = "+ Register New Account",
            Background = UiTheme.BgSurface,
            Foreground = UiTheme.Mauve,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
            IsExpanded = vm.Accounts.Count == 0
        };

        StackPanel addForm = new()
        {
            Spacing = 6,
            Margin = new Thickness(6, 4, 6, 6)
        };

        TextBox userBox = new()
        {
            Watermark = "Username (AQW login)",
            FontSize = 12
        };
        TextBox passBox = new()
        {
            Watermark = "Password",
            PasswordChar = '•',
            FontSize = 12
        };
        TextBox displayBox = new()
        {
            Watermark = "Display Nickname (optional)",
            FontSize = 12
        };

        Button saveBtn = new()
        {
            Content = "Save Account",
            Background = UiTheme.Mauve,
            Foreground = UiTheme.BgCrust,
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(14, 6)
        };
        saveBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(userBox.Text) || string.IsNullOrWhiteSpace(passBox.Text))
                return;

            vm.UsernameInput = userBox.Text.Trim();
            vm.PasswordInput = passBox.Text;
            vm.DisplayNameInput = string.IsNullOrWhiteSpace(displayBox.Text) ? userBox.Text.Trim() : displayBox.Text.Trim();
            vm.AddAccountCommand.Execute(null);

            userBox.Text = string.Empty;
            passBox.Text = string.Empty;
            displayBox.Text = string.Empty;
        };

        addForm.Children.Add(userBox);
        addForm.Children.Add(passBox);
        addForm.Children.Add(displayBox);
        addForm.Children.Add(saveBtn);
        addExpander.Content = addForm;

        Grid.SetRow(addExpander, 1);
        mainGrid.Children.Add(addExpander);

        // -------------------------------------------------------------
        // 3. ACCOUNTS LIST
        // -------------------------------------------------------------
        ListBox accountsList = new()
        {
            ItemsSource = vm.Accounts,
            Background = Brushes.Transparent,
            ItemTemplate = new FuncDataTemplate<AccountItemViewModel>((acc, _) =>
            {
                if (acc is null) return new TextBlock();

                Border card = new()
                {
                    Background = UiTheme.BgSurface,
                    BorderBrush = UiTheme.BorderSubtle,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                Grid row = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                CheckBox selectBox = new()
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                selectBox.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(AccountItemViewModel.UseCheck))
                {
                    Source = acc,
                    Mode = BindingMode.TwoWay
                });

                StackPanel infoStack = new()
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2
                };

                TextBlock nameText = new()
                {
                    Text = !string.IsNullOrEmpty(acc.DisplayName) ? acc.DisplayName : acc.Username,
                    Foreground = UiTheme.TextPrimary,
                    FontWeight = FontWeight.Bold,
                    FontSize = 13
                };

                TextBlock userSubText = new()
                {
                    Text = $"Login: {acc.Username}",
                    Foreground = UiTheme.TextDim,
                    FontSize = 11
                };

                infoStack.Children.Add(nameText);
                infoStack.Children.Add(userSubText);

                StackPanel actionsStack = new()
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Button launchBtn = new()
                {
                    Content = "▶ Launch",
                    Background = UiTheme.Blue,
                    Foreground = UiTheme.BgCrust,
                    FontWeight = FontWeight.Bold,
                    FontSize = 11.5,
                    Padding = new Thickness(10, 4),
                    Command = acc.StartCommand
                };

                Button removeBtn = new()
                {
                    Content = "✕",
                    Background = UiTheme.BgBase,
                    Foreground = UiTheme.Red,
                    FontWeight = FontWeight.Bold,
                    FontSize = 11.5,
                    Padding = new Thickness(8, 4),
                    Command = acc.RemoveCommand
                };

                actionsStack.Children.Add(launchBtn);
                actionsStack.Children.Add(removeBtn);

                Grid.SetColumn(selectBox, 0);
                Grid.SetColumn(infoStack, 1);
                Grid.SetColumn(actionsStack, 2);

                row.Children.Add(selectBox);
                row.Children.Add(infoStack);
                row.Children.Add(actionsStack);

                card.Child = row;
                return card;
            })
        };

        Grid.SetRow(accountsList, 2);
        mainGrid.Children.Add(accountsList);

        return mainGrid;
    }

    private static Control BuildSchedulerView(IServiceProvider services)
    {
        AccountSchedulerService scheduler = services.GetRequiredService<AccountSchedulerService>();
        AccountManagerViewModel vm = services.GetRequiredService<AccountManagerViewModel>();
        IFileDialogService fileDialog = services.GetRequiredService<IFileDialogService>();
        ScriptQueueService queueService = services.GetRequiredService<ScriptQueueService>();

        ScrollViewer scrollViewer = new()
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        StackPanel mainPanel = new()
        {
            Spacing = 10,
            Margin = new Thickness(8)
        };

        // 1. Scheduler Status Card
        StackPanel statusStack = new() { Spacing = 6 };
        TextBlock statusBadge = new()
        {
            Text = scheduler.IsActive ? "🟢 Monitorando Horários Diários" : "⏸ Agendador Pausado",
            Foreground = scheduler.IsActive ? UiTheme.Green : UiTheme.Yellow,
            FontWeight = FontWeight.Bold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button toggleSchedulerBtn = UiTheme.StyledButton(scheduler.IsActive ? "Pausar" : "Ativar", "subtle");
        toggleSchedulerBtn.Click += (_, _) =>
        {
            if (scheduler.IsActive)
                scheduler.StopMonitor();
            else
                scheduler.StartMonitor();

            statusBadge.Text = scheduler.IsActive ? "🟢 Monitorando Horários Diários" : "⏸ Agendador Pausado";
            statusBadge.Foreground = scheduler.IsActive ? UiTheme.Green : UiTheme.Yellow;
            toggleSchedulerBtn.Content = scheduler.IsActive ? "Pausar" : "Ativar";
        };

        DockPanel statusDock = new()
        {
            Children =
            {
                UiTheme.SectionTitle("⏰ Agendador Diário de Contas & Scripts", UiTheme.Mauve),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { statusBadge, toggleSchedulerBtn }
                }
            }
        };
        statusStack.Children.Add(statusDock);

        TextBlock descText = new()
        {
            Text = "Abra e execute automaticamente tarefas diárias em horários programados (ex: 04:00 após o reset do AQW).",
            Foreground = UiTheme.TextDim,
            FontSize = 11.5
        };
        statusStack.Children.Add(descText);
        mainPanel.Children.Add(UiTheme.Card(statusStack));

        // 2. Add New Scheduled Task Card
        StackPanel addPanel = new() { Spacing = 8 };
        addPanel.Children.Add(UiTheme.SectionTitle("➕ Nova Tarefa Agendada", UiTheme.Blue));

        ComboBox accountCombo = new()
        {
            ItemsSource = vm.Accounts,
            Width = 180,
            FontSize = 11.5,
            ItemTemplate = new FuncDataTemplate<AccountItemViewModel>((a, _) =>
                new TextBlock { Text = !string.IsNullOrEmpty(a?.DisplayName) ? a.DisplayName : a?.Username ?? "" })
        };
        if (vm.Accounts.Count > 0)
            accountCombo.SelectedIndex = 0;

        ComboBox serverCombo = new()
        {
            ItemsSource = vm.ServerList,
            Width = 140,
            FontSize = 11.5,
            ItemTemplate = new FuncDataTemplate<Server>((s, _) =>
                new TextBlock { Text = s?.Name ?? "" })
        };
        if (vm.ServerList.Count > 0)
            serverCombo.SelectedIndex = 0;

        TextBox timeBox = new()
        {
            Text = "04:00",
            Watermark = "HH:mm",
            Width = 70,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock timeLabel = new()
        {
            Text = "Horário:",
            FontSize = 11,
            Foreground = UiTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 2, 0)
        };

        WrapPanel row1 = new()
        {
            Children =
            {
                new TextBlock { Text = "Conta:", FontSize = 11, Foreground = UiTheme.TextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,4,0) },
                accountCombo,
                new TextBlock { Text = "Servidor:", FontSize = 11, Foreground = UiTheme.TextMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,4,0) },
                serverCombo,
                timeLabel,
                timeBox
            }
        };
        addPanel.Children.Add(row1);

        ComboBox modeCombo = new()
        {
            ItemsSource = new[] { "Script (.cs)", "Fila de Scripts" },
            SelectedIndex = 0,
            Width = 140,
            FontSize = 11.5
        };

        TextBox targetBox = new()
        {
            Watermark = "Caminho do Script (.cs) ou Nome da Fila...",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 11.5
        };

        Button browseTargetBtn = UiTheme.StyledButton("📂 Selecionar", "subtle");
        browseTargetBtn.Click += (_, _) =>
        {
            if (modeCombo.SelectedIndex == 0)
            {
                string? file = fileDialog.OpenFile(ClientFileSources.SkuaScriptsDIR, "C# Scripts (*.cs)|*.cs|All Files (*.*)|*.*");
                if (!string.IsNullOrEmpty(file))
                    targetBox.Text = file;
            }
            else
            {
                queueService.RefreshPresets();
                if (queueService.SavedPresets.Count > 0)
                    targetBox.Text = queueService.SavedPresets[0];
            }
        };

        Grid targetGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };
        Grid.SetColumn(modeCombo, 0);
        Grid.SetColumn(targetBox, 1);
        Grid.SetColumn(browseTargetBtn, 2);
        targetGrid.Children.Add(modeCombo);
        targetGrid.Children.Add(targetBox);
        targetGrid.Children.Add(browseTargetBtn);
        addPanel.Children.Add(targetGrid);

        CheckBox autoCloseCheck = new()
        {
            Content = "Fechar cliente ao terminar toda a execução (--auto-close)",
            FontSize = 11.5,
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button addTaskBtn = UiTheme.StyledButton("➕ Agendar Tarefa", "primary");
        addTaskBtn.Background = UiTheme.Blue;
        addTaskBtn.Foreground = Brushes.Black;
        addTaskBtn.FontWeight = FontWeight.Bold;
        addTaskBtn.Click += (_, _) =>
        {
            string accountName = (accountCombo.SelectedItem as AccountItemViewModel)?.DisplayName
                                 ?? (accountCombo.SelectedItem as AccountItemViewModel)?.Username
                                 ?? string.Empty;

            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(targetBox.Text))
                return;

            string serverName = (serverCombo.SelectedItem as Server)?.Name ?? "Twilly";
            string mode = modeCombo.SelectedIndex == 1 ? "Queue" : "Script";

            scheduler.AddTask(new ScheduledTask
            {
                AccountName = accountName,
                Server = serverName,
                ScheduledTime = timeBox.Text?.Trim() ?? "04:00",
                Mode = mode,
                TargetPath = targetBox.Text.Trim(),
                AutoClose = autoCloseCheck.IsChecked ?? true,
                IsEnabled = true
            });

            targetBox.Text = string.Empty;
        };

        DockPanel actionDock = new()
        {
            Children =
            {
                addTaskBtn,
                autoCloseCheck
            }
        };
        DockPanel.SetDock(addTaskBtn, Dock.Right);
        addPanel.Children.Add(actionDock);

        mainPanel.Children.Add(UiTheme.Card(addPanel));

        // 3. Tasks List Card
        StackPanel tasksListPanel = new() { Spacing = 8 };
        tasksListPanel.Children.Add(UiTheme.SectionTitle("Tarefas Agendadas", UiTheme.Yellow));

        ListBox tasksList = new()
        {
            ItemsSource = scheduler.Tasks,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<ScheduledTask>((task, _) =>
            {
                Border card = new()
                {
                    Background = UiTheme.BgSurface,
                    BorderBrush = UiTheme.BorderSubtle,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6),
                    Margin = new Thickness(0, 2)
                };

                Grid row = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
                };

                CheckBox enableBox = new()
                {
                    IsChecked = task.IsEnabled,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                enableBox.IsCheckedChanged += (_, _) =>
                {
                    task.IsEnabled = enableBox.IsChecked ?? false;
                    scheduler.SaveTasks();
                };

                StackPanel infoStack = new() { Spacing = 2 };
                WrapPanel badges = new() { Margin = new Thickness(0, 2) };
                badges.Children.Add(UiTheme.Badge($"⏰ {task.ScheduledTime}", UiTheme.Yellow));
                badges.Children.Add(UiTheme.Badge($"👤 {task.AccountName}", UiTheme.Mauve));
                badges.Children.Add(UiTheme.Badge($"🌐 {task.Server}", UiTheme.Teal));
                if (task.AutoClose)
                    badges.Children.Add(UiTheme.Badge("AutoClose", UiTheme.TextDim));

                TextBlock targetText = new()
                {
                    Text = $"{(task.Mode == "Queue" ? "📋 Fila" : "📜 Script")}: {task.TargetPath}",
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                    Foreground = UiTheme.TextPrimary
                };

                TextBlock lastRunText = new()
                {
                    Text = $"Última execução: {task.LastRun}",
                    FontSize = 10.5,
                    Foreground = UiTheme.TextMuted
                };

                infoStack.Children.Add(badges);
                infoStack.Children.Add(targetText);
                infoStack.Children.Add(lastRunText);

                StackPanel actions = new()
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Button runNowBtn = UiTheme.StyledButton("▶ Testar Agora", "subtle");
                runNowBtn.Click += (_, _) => scheduler.LaunchScheduledTask(task);

                Button delBtn = UiTheme.StyledButton("🗑", "subtle");
                delBtn.Foreground = UiTheme.Red;
                delBtn.Click += (_, _) => scheduler.RemoveTask(task);

                actions.Children.Add(runNowBtn);
                actions.Children.Add(delBtn);

                Grid.SetColumn(enableBox, 0);
                Grid.SetColumn(infoStack, 1);
                Grid.SetColumn(actions, 2);

                row.Children.Add(enableBox);
                row.Children.Add(infoStack);
                row.Children.Add(actions);

                card.Child = row;
                return card;
            })
        };
        tasksListPanel.Children.Add(tasksList);
        mainPanel.Children.Add(UiTheme.Card(tasksListPanel));

        scrollViewer.Content = mainPanel;
        return scrollViewer;
    }
}
