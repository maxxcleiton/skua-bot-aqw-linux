using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.ViewModels;

namespace Skua.Linux.UI;

internal static class RuntimeAndTravelTabs
{
    public static Control BuildRuntimeTab(IServiceProvider services)
    {
        RuntimeHelpersViewModel runtimeVm = services.GetRequiredService<RuntimeHelpersViewModel>();
        CurrentDropsViewModel currentDropsVm = services.GetRequiredService<CurrentDropsViewModel>();
        runtimeVm.IsActive = true;
        currentDropsVm.IsActive = true;

        StackPanel panel = new()
        {
            Margin = new Thickness(8),
            Spacing = 10
        };

        // 1. Current Drops
        ListBox currentDropsList = new()
        {
            Height = 125,
            ItemsSource = currentDropsVm.CurrentDrops,
            ItemTemplate = new FuncDataTemplate<ItemBase>((item, _) =>
            {
                if (item is null) return new TextBlock();
                Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                TextBlock name = new()
                {
                    Text = $"{item.Name} x{item.Quantity}",
                    Foreground = item.Coins ? UiTheme.Yellow : UiTheme.TextPrimary,
                    FontSize = 12
                };
                TextBlock tag = new()
                {
                    Text = item.Coins ? "AC" : $"ID:{item.ID}",
                    Foreground = item.Coins ? UiTheme.Peach : UiTheme.TextDim,
                    FontSize = 11,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(name, 0);
                Grid.SetColumn(tag, 1);
                row.Children.Add(name);
                row.Children.Add(tag);
                return row;
            })
        };
        currentDropsList.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(CurrentDropsViewModel.SelectedDrop)) { Source = currentDropsVm, Mode = BindingMode.TwoWay });
        currentDropsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CurrentDropsViewModel.CurrentDrops))
                Dispatcher.UIThread.Post(() => currentDropsList.ItemsSource = currentDropsVm.CurrentDrops);
        };

        Button pickupSelBtn = UiTheme.StyledButton("Pickup Selected", "primary");
        pickupSelBtn.Command = currentDropsVm.PickupCommand;
        Button pickupAcBtn = UiTheme.StyledButton("Pickup AC", "accent");
        pickupAcBtn.Command = currentDropsVm.PickupACCommand;
        Button pickupAllBtn = UiTheme.StyledButton("Pickup All", "success");
        pickupAllBtn.Command = currentDropsVm.PickupAllCommand;

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                UiTheme.SectionTitle("Current Drops on Screen", UiTheme.Green),
                currentDropsList,
                new WrapPanel { Children = { pickupSelBtn, pickupAcBtn, pickupAllBtn } }
            }
        }));

        // 2. Auto-Pickup Whitelist
        ToPickupDropsViewModel dropsVm = runtimeVm.ToPickupDropsViewModel;

        TextBox dropInput = new()
        {
            Watermark = "Item names or IDs (separate with |)",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        dropInput.Bind(TextBox.TextProperty, new Binding(nameof(ToPickupDropsViewModel.AddDropInput)) { Source = dropsVm, Mode = BindingMode.TwoWay });

        Button addDropBtn = UiTheme.StyledButton("Add", "primary");
        addDropBtn.Command = dropsVm.AddDropCommand;

        Grid dropInputRow = new() { ColumnDefinitions = new ColumnDefinitions("*,6,Auto") };
        Grid.SetColumn(dropInput, 0);
        Grid.SetColumn(addDropBtn, 2);
        dropInputRow.Children.Add(dropInput);
        dropInputRow.Children.Add(addDropBtn);

        ListBox whitelistBox = new()
        {
            Height = 105,
            ItemsSource = dropsVm.ToPickup
        };
        dropsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ToPickupDropsViewModel.ToPickup))
                Dispatcher.UIThread.Post(() => whitelistBox.ItemsSource = dropsVm.ToPickup);
        };

        Button toggleDropsBtn = UiTheme.StyledButton(dropsVm.Drops.Enabled ? "Stop Drops" : "Start Drops", dropsVm.Drops.Enabled ? "danger" : "success");
        toggleDropsBtn.Command = dropsVm.ToggleDropsCommand;
        dropsVm.Drops.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IScriptDrop.Enabled))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    bool on = dropsVm.Drops.Enabled;
                    toggleDropsBtn.Content = on ? "Stop Drops" : "Start Drops";
                    toggleDropsBtn.Classes.Set("danger", on);
                    toggleDropsBtn.Classes.Set("success", !on);
                });
            }
        };

        Button removeDropBtn = UiTheme.StyledButton("Remove Selected", "subtle");
        removeDropBtn.Click += (_, _) =>
        {
            if (whitelistBox.SelectedItem is not null)
                dropsVm.RemoveDropsCommand.Execute(new List<object> { whitelistBox.SelectedItem });
        };

        Button clearDropsBtn = UiTheme.StyledButton("Clear All", "subtle");
        clearDropsBtn.Command = dropsVm.RemoveAllDropsCommand;

        CheckBox rejectElseChk = new() { Content = "Reject Else", Margin = new Thickness(4, 0) };
        rejectElseChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(IScriptDrop.RejectElse)) { Source = dropsVm.Drops, Mode = BindingMode.TwoWay });

        CheckBox acceptAcChk = new() { Content = "Accept AC", Margin = new Thickness(4, 0) };
        acceptAcChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(IScriptOption.AcceptACDrops)) { Source = dropsVm.Options, Mode = BindingMode.TwoWay });

        CheckBox acceptAllChk = new() { Content = "Accept All", Margin = new Thickness(4, 0) };
        acceptAllChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(IScriptOption.AcceptAllDrops)) { Source = dropsVm.Options, Mode = BindingMode.TwoWay });

        CheckBox rejectAllChk = new() { Content = "Reject All", Margin = new Thickness(4, 0) };
        rejectAllChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(IScriptOption.RejectAllDrops)) { Source = dropsVm.Options, Mode = BindingMode.TwoWay });

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                UiTheme.SectionTitle("Auto-Pickup Drop Whitelist", UiTheme.Blue),
                dropInputRow,
                whitelistBox,
                new WrapPanel { Children = { toggleDropsBtn, removeDropBtn, clearDropsBtn } },
                new WrapPanel { Children = { rejectElseChk, acceptAcChk, acceptAllChk, rejectAllChk } }
            }
        }));

        // 3. Auto Boosts
        BoostsViewModel boostsVm = runtimeVm.BoostsViewModel;

        CheckBox xpChk = new() { Content = "XP Boost", Margin = new Thickness(4, 0) };
        xpChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(IScriptBoost.UseExperienceBoost)) { Source = boostsVm.Boosts, Mode = BindingMode.TwoWay });
        CheckBox goldChk = new() { Content = "Gold Boost", Margin = new Thickness(4, 0) };
        goldChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(IScriptBoost.UseGoldBoost)) { Source = boostsVm.Boosts, Mode = BindingMode.TwoWay });
        CheckBox classChk = new() { Content = "Class Boost", Margin = new Thickness(4, 0) };
        classChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(IScriptBoost.UseClassBoost)) { Source = boostsVm.Boosts, Mode = BindingMode.TwoWay });
        CheckBox repChk = new() { Content = "Rep Boost", Margin = new Thickness(4, 0) };
        repChk.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(IScriptBoost.UseReputationBoost)) { Source = boostsVm.Boosts, Mode = BindingMode.TwoWay });

        CheckBox searchBankChk = new() { Content = "Search Bank", IsChecked = true, Margin = new Thickness(4, 0) };

        Button detectBoostsBtn = UiTheme.StyledButton("Auto-Detect Boosts", "primary");
        detectBoostsBtn.Click += (_, _) => boostsVm.SetBoostIDsCommand.Execute(searchBankChk.IsChecked == true);

        Button toggleBoostsBtn = UiTheme.StyledButton(boostsVm.Boosts.Enabled ? "Stop Boosts" : "Start Boosts", boostsVm.Boosts.Enabled ? "danger" : "success");
        toggleBoostsBtn.Command = boostsVm.ToggleBoostsCommand;
        boostsVm.Boosts.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IScriptBoost.Enabled))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    bool on = boostsVm.Boosts.Enabled;
                    toggleBoostsBtn.Content = on ? "Stop Boosts" : "Start Boosts";
                    toggleBoostsBtn.Classes.Set("danger", on);
                    toggleBoostsBtn.Classes.Set("success", !on);
                });
            }
        };

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                UiTheme.SectionTitle("Auto Boosts", UiTheme.Yellow),
                new WrapPanel { Children = { xpChk, goldChk, classChk, repChk } },
                new WrapPanel { Children = { toggleBoostsBtn, detectBoostsBtn, searchBankChk } }
            }
        }));

        // 4. Registered Quests
        RegisteredQuestsViewModel questsVm = runtimeVm.RegisteredQuestsViewModel;

        TextBox questIdBox = new() { Watermark = "Quest IDs (e.g. 123, 456)", Width = 180, Margin = new Thickness(0, 0, 4, 0) };
        questIdBox.Bind(TextBox.TextProperty, new Binding(nameof(RegisteredQuestsViewModel.AddQuestInput)) { Source = questsVm, Mode = BindingMode.TwoWay });

        TextBox rewardIdBox = new() { Watermark = "Reward ID (opt.)", Width = 115, Margin = new Thickness(0, 0, 4, 0) };
        rewardIdBox.Bind(TextBox.TextProperty, new Binding(nameof(RegisteredQuestsViewModel.RewardIdInput)) { Source = questsVm, Mode = BindingMode.TwoWay });

        Button addQuestBtn = UiTheme.StyledButton("Register", "primary");
        addQuestBtn.Command = questsVm.AddQuestCommand;

        ListBox questsList = new()
        {
            Height = 95,
            ItemsSource = questsVm.CurrentAutoQuests,
            ItemTemplate = new FuncDataTemplate<RegisteredQuestInfo>((q, _) =>
                new TextBlock
                {
                    Text = q is null ? string.Empty : (q.RewardId > 0 ? $"Quest #{q.QuestId}  →  Reward ID {q.RewardId}" : $"Quest #{q.QuestId}"),
                    Foreground = UiTheme.TextPrimary,
                    FontSize = 12
                })
        };
        questsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RegisteredQuestsViewModel.CurrentAutoQuests))
                Dispatcher.UIThread.Post(() => questsList.ItemsSource = questsVm.CurrentAutoQuests);
        };

        Button removeQuestBtn = UiTheme.StyledButton("Remove Selected", "subtle");
        removeQuestBtn.Click += (_, _) =>
        {
            if (questsList.SelectedItem is not null)
                questsVm.RemoveQuestsCommand.Execute(new List<object> { questsList.SelectedItem });
        };
        Button clearQuestsBtn = UiTheme.StyledButton("Clear All", "subtle");
        clearQuestsBtn.Command = questsVm.RemoveAllQuestsCommand;

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                UiTheme.SectionTitle("Auto-Registered Quests", UiTheme.Mauve),
                new WrapPanel { Children = { questIdBox, rewardIdBox, addQuestBtn } },
                questsList,
                new WrapPanel { Children = { removeQuestBtn, clearQuestsBtn } }
            }
        }));

        return new ScrollViewer { Content = panel };
    }

    public static Control BuildAutoAndMapTab(IServiceProvider services)
    {
        LoaderViewModel loaderVm = services.GetRequiredService<LoaderViewModel>();
        JumpViewModel jumpVm = services.GetRequiredService<JumpViewModel>();
        AutoViewModel autoVm = services.GetRequiredService<AutoViewModel>();
        FastTravelViewModel ftVm = services.GetRequiredService<FastTravelViewModel>();
        ftVm.IsActive = true;

        StackPanel panel = new()
        {
            Margin = new Thickness(8),
            Spacing = 10
        };

        // 1. Auto Attack / Auto Hunt with Class & Mode Selection
        ComboBox classBox = new()
        {
            PlaceholderText = "Select Class (Optional)",
            ItemsSource = autoVm.PlayerClasses,
            Width = 200,
            Margin = new Thickness(0, 0, 6, 4)
        };
        classBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(AutoViewModel.SelectedClass)) { Source = autoVm, Mode = BindingMode.TwoWay });

        ComboBox modeBox = new()
        {
            PlaceholderText = "Class Mode",
            ItemsSource = autoVm.CurrentClassModeStrings,
            Width = 140,
            Margin = new Thickness(0, 0, 6, 4)
        };
        modeBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(AutoViewModel.SelectedClassModeString)) { Source = autoVm, Mode = BindingMode.TwoWay });

        autoVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AutoViewModel.PlayerClasses))
                Dispatcher.UIThread.Post(() => classBox.ItemsSource = autoVm.PlayerClasses);
            else if (e.PropertyName == nameof(AutoViewModel.CurrentClassModeStrings))
                Dispatcher.UIThread.Post(() => modeBox.ItemsSource = autoVm.CurrentClassModeStrings);
        };

        Button reloadClassesBtn = UiTheme.StyledButton("Refresh Classes", "subtle");
        reloadClassesBtn.Command = autoVm.ReloadClassesCommand;

        TextBox manualIdsBox = new()
        {
            Watermark = "Optional Monster MapIDs (e.g. 1, 2, 3)",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        manualIdsBox.Bind(TextBox.TextProperty, new Binding(nameof(AutoViewModel.ManualMapIDs)) { Source = autoVm, Mode = BindingMode.TwoWay });

        Button autoAtkBtn = UiTheme.StyledButton("Start Auto-Attack", "primary");
        autoAtkBtn.Command = autoVm.StartAutoAttackCommand;
        Button autoHuntBtn = UiTheme.StyledButton("Start Auto-Hunt", "success");
        autoHuntBtn.Command = autoVm.StartAutoHuntCommand;
        Button stopAutoBtn = UiTheme.StyledButton("Stop Auto", "danger");
        stopAutoBtn.Command = autoVm.StopAutoAsyncCommand;

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                UiTheme.SectionTitle("Auto Attack / Auto Hunt", UiTheme.Green),
                new WrapPanel { Children = { classBox, modeBox, reloadClassesBtn } },
                manualIdsBox,
                new WrapPanel { Children = { autoAtkBtn, autoHuntBtn, stopAutoBtn } }
            }
        }));

        // 2. Fast Travel
        ListBox ftList = new()
        {
            Height = 145,
            ItemsSource = ftVm.FastTravelItems,
            ItemTemplate = new FuncDataTemplate<FastTravelItemViewModel>((item, _) =>
            {
                if (item is null) return new TextBlock();
                Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

                StackPanel info = new() { Spacing = 1 };
                info.Children.Add(new TextBlock
                {
                    Text = item.DescriptionName,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = UiTheme.TextPrimary,
                    FontSize = 12
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"{item.MapName} ({item.Cell} / {item.Pad})",
                    Foreground = UiTheme.TextMuted,
                    FontSize = 10.5
                });
                Grid.SetColumn(info, 0);

                Button goBtn = UiTheme.StyledButton("Go", "primary", new Thickness(4, 0, 2, 0));
                goBtn.Padding = new Thickness(10, 3);
                goBtn.Click += (_, _) => item.TravelCommand.Execute(item);
                Grid.SetColumn(goBtn, 1);

                Button delBtn = UiTheme.StyledButton("✕", "subtle", new Thickness(2, 0, 0, 0));
                delBtn.Padding = new Thickness(6, 3);
                delBtn.Click += (_, _) => item.RemoveCommand.Execute(null);
                Grid.SetColumn(delBtn, 2);

                row.Children.Add(info);
                row.Children.Add(goBtn);
                row.Children.Add(delBtn);
                return row;
            })
        };

        TextBox ftDesc = new() { Watermark = "Name (e.g. Battleon)", Width = 140, Margin = new Thickness(0, 0, 4, 4) };
        ftDesc.Bind(TextBox.TextProperty, new Binding("Travel.DescriptionName") { Source = ftVm.Editor, Mode = BindingMode.TwoWay });
        TextBox ftMap = new() { Watermark = "Map", Width = 110, Margin = new Thickness(0, 0, 4, 4) };
        ftMap.Bind(TextBox.TextProperty, new Binding("Travel.MapName") { Source = ftVm.Editor, Mode = BindingMode.TwoWay });
        TextBox ftCell = new() { Watermark = "Cell", Width = 85, Margin = new Thickness(0, 0, 4, 4) };
        ftCell.Bind(TextBox.TextProperty, new Binding("Travel.Cell") { Source = ftVm.Editor, Mode = BindingMode.TwoWay });
        TextBox ftPad = new() { Watermark = "Pad", Width = 85, Margin = new Thickness(0, 0, 4, 4) };
        ftPad.Bind(TextBox.TextProperty, new Binding("Travel.Pad") { Source = ftVm.Editor, Mode = BindingMode.TwoWay });

        Button ftCurrentBtn = UiTheme.StyledButton("Get Current", "subtle");
        ftCurrentBtn.Command = ftVm.Editor.GetCurrentCommand;
        Button ftAddBtn = UiTheme.StyledButton("Add Travel", "success");
        ftAddBtn.Command = ftVm.AddFastTravelCommand;
        Button ftClearBtn = UiTheme.StyledButton("Clear", "subtle");
        ftClearBtn.Command = ftVm.ClearFastTravelsCommand;

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                UiTheme.SectionTitle("Fast Travel", UiTheme.Teal),
                ftList,
                new WrapPanel { Children = { ftDesc, ftMap, ftCell, ftPad } },
                new WrapPanel { Children = { ftCurrentBtn, ftAddBtn, ftClearBtn } }
            }
        }));

        // 3. Cell / Pad Jump
        ComboBox cellsBox = new() { ItemsSource = jumpVm.Cells, Width = 145, Margin = new Thickness(0, 0, 6, 0), PlaceholderText = "Cell" };
        cellsBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(JumpViewModel.SelectedCell)) { Source = jumpVm, Mode = BindingMode.TwoWay });

        ComboBox padsBox = new() { ItemsSource = jumpVm.Pads, Width = 120, Margin = new Thickness(0, 0, 6, 0), PlaceholderText = "Pad" };
        padsBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(JumpViewModel.SelectedPad)) { Source = jumpVm, Mode = BindingMode.TwoWay });

        Button refreshCellsBtn = UiTheme.StyledButton("Refresh", "subtle");
        refreshCellsBtn.Command = jumpVm.UpdateCellsCommand;
        Button currentCellBtn = UiTheme.StyledButton("Current", "subtle");
        currentCellBtn.Command = jumpVm.GetCurrentCommand;
        Button jumpBtn = UiTheme.StyledButton("Jump", "primary");
        jumpBtn.Command = jumpVm.JumpToCommand;

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                UiTheme.SectionTitle("Cell & Pad Jump", UiTheme.Blue),
                new WrapPanel { Children = { cellsBox, padsBox, refreshCellsBtn, currentCellBtn, jumpBtn } }
            }
        }));

        // 4. Shop / Quest Loader
        ComboBox loadTypeBox = new()
        {
            ItemsSource = new[] { "Shop ID", "Quest ID(s)" },
            SelectedIndex = 0,
            Width = 120,
            Margin = new Thickness(0, 0, 6, 0)
        };
        loadTypeBox.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(LoaderViewModel.SelectedIndex)) { Source = loaderVm, Mode = BindingMode.TwoWay });

        TextBox loadIdsBox = new()
        {
            Watermark = "Shop ID or comma-separated Quest IDs...",
            Width = 210,
            Margin = new Thickness(0, 0, 6, 0)
        };
        loadIdsBox.Bind(TextBox.TextProperty, new Binding(nameof(LoaderViewModel.InputIDs)) { Source = loaderVm, Mode = BindingMode.TwoWay });

        Button loadNowBtn = UiTheme.StyledButton("Load Now", "accent");
        loadNowBtn.Command = loaderVm.LoadCommand;

        panel.Children.Add(UiTheme.Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                UiTheme.SectionTitle("Shop & Quest Loader", UiTheme.Mauve),
                new WrapPanel { Children = { loadTypeBox, loadIdsBox, loadNowBtn } }
            }
        }));

        return new ScrollViewer { Content = panel };
    }
}
