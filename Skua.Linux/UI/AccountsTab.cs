using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Models.Servers;
using Skua.Core.ViewModels;
using Skua.Core.ViewModels.Manager;

namespace Skua.Linux.UI;

internal static class AccountsTab
{
    public static Control BuildAccountsTab(IServiceProvider services)
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
}
