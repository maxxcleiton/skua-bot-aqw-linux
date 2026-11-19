using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Utils;
using Skua.Linux.Services;

namespace Skua.Linux.UI;

public static class ScriptQueueTab
{
    public static Control BuildQueueTab(IServiceProvider services)
    {
        ScriptQueueService queueService = services.GetRequiredService<ScriptQueueService>();
        IFileDialogService fileDialog = services.GetRequiredService<IFileDialogService>();

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

        // 1. Controls & Preset Card
        mainPanel.Children.Add(BuildControlsCard(queueService));

        // 2. Add New Script to Queue Card
        mainPanel.Children.Add(BuildAddItemCard(queueService, fileDialog));

        // 3. Queue Execution List Card
        mainPanel.Children.Add(BuildQueueItemsCard(queueService));

        scrollViewer.Content = mainPanel;
        return scrollViewer;
    }

    private static Border BuildControlsCard(ScriptQueueService queueService)
    {
        StackPanel panel = new() { Spacing = 8 };

        // Title and live status summary
        TextBlock statusSummary = new()
        {
            Text = queueService.StatusSummary,
            Foreground = UiTheme.Green,
            FontWeight = FontWeight.Bold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        queueService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScriptQueueService.StatusSummary))
            {
                Dispatcher.UIThread.Post(() => statusSummary.Text = queueService.StatusSummary);
            }
        };

        panel.Children.Add(new DockPanel
        {
            Children =
            {
                UiTheme.SectionTitle("📋 Fila de Scripts & Pipeline Sequencial", UiTheme.Mauve),
                statusSummary
            }
        });

        // Presets row: Save / Load / Delete
        TextBox presetNameBox = new()
        {
            Watermark = "Nome da Fila (ex: Dailies e Farm)",
            Width = 190,
            FontSize = 11.5
        };

        ComboBox presetCombo = new()
        {
            ItemsSource = queueService.SavedPresets,
            Width = 160,
            FontSize = 11.5
        };

        Button saveBtn = UiTheme.StyledButton("💾 Salvar", "subtle");
        saveBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(presetNameBox.Text))
                queueService.SavePreset(presetNameBox.Text);
        };

        Button loadBtn = UiTheme.StyledButton("📂 Carregar", "subtle");
        loadBtn.Click += (_, _) =>
        {
            if (presetCombo.SelectedItem is string selected)
            {
                queueService.LoadPreset(selected);
                presetNameBox.Text = selected;
            }
        };

        Button delBtn = UiTheme.StyledButton("🗑", "subtle");
        delBtn.Click += (_, _) =>
        {
            if (presetCombo.SelectedItem is string selected)
                queueService.DeletePreset(selected);
        };

        WrapPanel presetRow = new()
        {
            Margin = new Thickness(0, 2),
            Children =
            {
                presetNameBox,
                saveBtn,
                presetCombo,
                loadBtn,
                delBtn
            }
        };
        panel.Children.Add(presetRow);

        // Action Buttons Row
        Button startBtn = UiTheme.StyledButton("▶ Iniciar Fila", "primary");
        startBtn.Background = UiTheme.Green;
        startBtn.Foreground = Brushes.Black;
        startBtn.FontWeight = FontWeight.Bold;
        startBtn.Click += (_, _) => queueService.StartQueue();

        Button stopBtn = UiTheme.StyledButton("⏹ Parar Fila", "subtle");
        stopBtn.Click += (_, _) => queueService.StopQueue();

        Button skipBtn = UiTheme.StyledButton("⏭ Pular Atual", "subtle");
        skipBtn.Click += (_, _) => queueService.SkipCurrent();

        Button clearBtn = UiTheme.StyledButton("🗑 Limpar Lista", "subtle");
        clearBtn.Click += (_, _) => queueService.Clear();

        WrapPanel actionRow = new()
        {
            Margin = new Thickness(0, 4),
            Children = { startBtn, stopBtn, skipBtn, clearBtn }
        };
        panel.Children.Add(actionRow);

        // Options: AutoLogout and AutoClose checkboxes
        CheckBox autoCloseCheck = new()
        {
            Content = "Fechar Skua ao concluir toda a fila (--auto-close)",
            FontSize = 11.5,
            Margin = new Thickness(2, 0, 10, 0),
            IsChecked = queueService.AutoCloseWhenFinished
        };
        autoCloseCheck.IsCheckedChanged += (_, _) =>
            queueService.AutoCloseWhenFinished = autoCloseCheck.IsChecked ?? false;

        CheckBox autoLogoutCheck = new()
        {
            Content = "Deslogar da conta ao concluir toda a fila",
            FontSize = 11.5,
            IsChecked = queueService.AutoLogoutWhenFinished
        };
        autoLogoutCheck.IsCheckedChanged += (_, _) =>
            queueService.AutoLogoutWhenFinished = autoLogoutCheck.IsChecked ?? false;

        WrapPanel optionsRow = new()
        {
            Children = { autoCloseCheck, autoLogoutCheck }
        };
        panel.Children.Add(optionsRow);

        return UiTheme.Card(panel);
    }

    private static Border BuildAddItemCard(ScriptQueueService queueService, IFileDialogService fileDialog)
    {
        StackPanel panel = new() { Spacing = 8 };

        panel.Children.Add(UiTheme.SectionTitle("➕ Adicionar Script à Fila", UiTheme.Blue));

        TextBox scriptBox = new()
        {
            Watermark = "Caminho do Script (.cs)...",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 11.5
        };

        Button browseBtn = UiTheme.StyledButton("📂 Selecionar (.cs)", "subtle");
        browseBtn.Click += (_, _) =>
        {
            string scriptsDir = ClientFileSources.SkuaScriptsDIR;
            string? file = fileDialog.OpenFile(scriptsDir, "C# Scripts (*.cs)|*.cs|All Files (*.*)|*.*");
            if (!string.IsNullOrEmpty(file))
                scriptBox.Text = file;
        };

        Grid scriptRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        Grid.SetColumn(scriptBox, 0);
        Grid.SetColumn(browseBtn, 1);
        scriptRow.Children.Add(scriptBox);
        scriptRow.Children.Add(browseBtn);
        panel.Children.Add(scriptRow);

        // Parameters row: Class name, Smart enhance, Cooldown delay
        TextBox classBox = new()
        {
            Watermark = "Classe para equipar (opcional, ex: ArchPaladin)",
            Width = 240,
            FontSize = 11.5
        };

        CheckBox smartEnhanceCheck = new()
        {
            Content = "Smart Enhance",
            FontSize = 11.5,
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0)
        };

        TextBox delayBox = new()
        {
            Text = "3",
            Watermark = "Cooldown (s)",
            Width = 45,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock delayLabel = new()
        {
            Text = "Cooldown (s):",
            FontSize = 11,
            Foreground = UiTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 3, 0)
        };

        Button addBtn = UiTheme.StyledButton("➕ Adicionar à Ordem", "primary");
        addBtn.Background = UiTheme.Blue;
        addBtn.Foreground = Brushes.Black;
        addBtn.FontWeight = FontWeight.Bold;
        addBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(scriptBox.Text))
                return;

            int delay = int.TryParse(delayBox.Text, out int d) ? d : 3;
            queueService.AddItem(
                scriptBox.Text,
                classBox.Text ?? string.Empty,
                smartEnhanceCheck.IsChecked ?? false,
                delay);

            scriptBox.Text = string.Empty;
        };

        WrapPanel paramsRow = new()
        {
            Children =
            {
                classBox,
                smartEnhanceCheck,
                delayLabel,
                delayBox,
                addBtn
            }
        };
        panel.Children.Add(paramsRow);

        return UiTheme.Card(panel);
    }

    private static Border BuildQueueItemsCard(ScriptQueueService queueService)
    {
        StackPanel panel = new() { Spacing = 8 };

        panel.Children.Add(UiTheme.SectionTitle("Ordem de Execução Sequencial", UiTheme.Yellow));

        ListBox itemsList = new()
        {
            ItemsSource = queueService.Items,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<ScriptQueueItem>((item, _) =>
            {
                Border rowCard = new()
                {
                    Background = UiTheme.BgSurface,
                    BorderBrush = UiTheme.BorderSubtle,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6),
                    Margin = new Thickness(0, 2)
                };

                Grid rowGrid = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto")
                };

                // Index badge
                TextBlock indexText = new()
                {
                    FontWeight = FontWeight.Bold,
                    FontSize = 12,
                    Foreground = UiTheme.Mauve,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                indexText.Text = $"#{queueService.Items.IndexOf(item) + 1}";

                // Script name and details
                StackPanel infoPanel = new() { Spacing = 2 };
                TextBlock titleText = new()
                {
                    Text = item.DisplayName,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12.5,
                    Foreground = UiTheme.TextPrimary
                };

                WrapPanel tagsPanel = new() { Margin = new Thickness(0, 2) };
                if (!string.IsNullOrWhiteSpace(item.ClassName))
                {
                    tagsPanel.Children.Add(UiTheme.Badge($"🛡 {item.ClassName}", UiTheme.Peach));
                }
                if (item.EnableSmartEnhance)
                {
                    tagsPanel.Children.Add(UiTheme.Badge("⚡ Smart Enhance", UiTheme.Teal));
                }
                if (item.DelaySeconds > 0)
                {
                    tagsPanel.Children.Add(UiTheme.Badge($"⏳ {item.DelaySeconds}s delay", UiTheme.TextMuted));
                }

                infoPanel.Children.Add(titleText);
                infoPanel.Children.Add(tagsPanel);

                // Status Badge
                TextBlock statusBadge = new()
                {
                    Text = item.StatusMessage,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0)
                };
                statusBadge.Foreground = item.Status switch
                {
                    QueueItemStatus.Running => UiTheme.Green,
                    QueueItemStatus.Completed => UiTheme.Teal,
                    QueueItemStatus.Failed => UiTheme.Red,
                    QueueItemStatus.Skipped => UiTheme.Yellow,
                    _ => UiTheme.TextMuted
                };

                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(ScriptQueueItem.StatusMessage) or nameof(ScriptQueueItem.Status))
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            statusBadge.Text = item.StatusMessage;
                            statusBadge.Foreground = item.Status switch
                            {
                                QueueItemStatus.Running => UiTheme.Green,
                                QueueItemStatus.Completed => UiTheme.Teal,
                                QueueItemStatus.Failed => UiTheme.Red,
                                QueueItemStatus.Skipped => UiTheme.Yellow,
                                _ => UiTheme.TextMuted
                            };
                        });
                    }
                };

                // Move buttons
                Button upBtn = UiTheme.StyledButton("▲", "subtle");
                upBtn.Click += (_, _) => queueService.MoveUp(item);

                Button downBtn = UiTheme.StyledButton("▼", "subtle");
                downBtn.Click += (_, _) => queueService.MoveDown(item);

                Button removeBtn = UiTheme.StyledButton("✕", "subtle");
                removeBtn.Foreground = UiTheme.Red;
                removeBtn.Click += (_, _) => queueService.RemoveItem(item);

                StackPanel btnPanel = new()
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { upBtn, downBtn, removeBtn }
                };

                Grid.SetColumn(indexText, 0);
                Grid.SetColumn(infoPanel, 1);
                Grid.SetColumn(statusBadge, 2);
                Grid.SetColumn(btnPanel, 3);

                rowGrid.Children.Add(indexText);
                rowGrid.Children.Add(infoPanel);
                rowGrid.Children.Add(statusBadge);
                rowGrid.Children.Add(btnPanel);

                rowCard.Child = rowGrid;
                return rowCard;
            })
        };

        panel.Children.Add(itemsList);
        return UiTheme.Card(panel);
    }
}
