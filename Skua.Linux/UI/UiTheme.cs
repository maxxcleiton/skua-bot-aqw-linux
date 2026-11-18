using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Skua.Linux.UI;

internal static class UiTheme
{
    public static readonly IBrush BgCrust = new SolidColorBrush(Color.Parse("#11111B"));
    public static readonly IBrush BgMantle = new SolidColorBrush(Color.Parse("#181825"));
    public static readonly IBrush BgBase = new SolidColorBrush(Color.Parse("#1E1E2E"));
    public static readonly IBrush BgSurface = new SolidColorBrush(Color.Parse("#252638"));
    public static readonly IBrush BorderSubtle = new SolidColorBrush(Color.Parse("#313244"));
    public static readonly IBrush TextPrimary = new SolidColorBrush(Color.Parse("#CDD6F4"));
    public static readonly IBrush TextMuted = new SolidColorBrush(Color.Parse("#A6ADC8"));
    public static readonly IBrush TextDim = new SolidColorBrush(Color.Parse("#6C7086"));

    public static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#89B4FA"));
    public static readonly IBrush Mauve = new SolidColorBrush(Color.Parse("#CBA6F7"));
    public static readonly IBrush Green = new SolidColorBrush(Color.Parse("#A6E3A1"));
    public static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#F9E2AF"));
    public static readonly IBrush Peach = new SolidColorBrush(Color.Parse("#FAB387"));
    public static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F38BA8"));
    public static readonly IBrush Teal = new SolidColorBrush(Color.Parse("#94E2D5"));

    public static readonly FontFamily MonoFont = new("monospace");

    public static Border Card(Control child, Thickness? padding = null, Thickness? margin = null)
    {
        Border border = new()
        {
            Child = child,
            Margin = margin ?? new Thickness(0)
        };
        border.Classes.Add("card");
        if (padding.HasValue)
            border.Padding = padding.Value;
        return border;
    }

    public static TextBlock SectionTitle(string text, IBrush? color = null)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.Bold,
            FontSize = 12.5,
            Foreground = color ?? Blue,
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    public static Border Badge(string text, IBrush foreground, IBrush? background = null)
    {
        return new Border
        {
            Background = background ?? BgSurface,
            BorderBrush = BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold
            }
        };
    }

    public static Button StyledButton(string text, string? styleClass = null, Thickness? margin = null)
    {
        Button btn = new()
        {
            Content = text,
            Margin = margin ?? new Thickness(2)
        };
        if (!string.IsNullOrEmpty(styleClass))
            btn.Classes.Add(styleClass);
        return btn;
    }
}
