using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;

namespace System.Windows.Forms;

public enum DialogResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7
}

public enum MessageBoxButtons
{
    OK = 0,
    OKCancel = 1,
    AbortRetryIgnore = 2,
    YesNoCancel = 3,
    YesNo = 4,
    RetryCancel = 5
}

public enum MessageBoxIcon
{
    None = 0,
    Hand = 16,
    Error = 16,
    Stop = 16,
    Question = 32,
    Exclamation = 48,
    Warning = 48,
    Asterisk = 64,
    Information = 64
}

public static class MessageBox
{
    public static DialogResult Show(string? text, string? caption = "Skua", MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
    {
        try
        {
            IDialogService? dialog = Ioc.Default.GetService<IDialogService>();
            if (dialog is not null)
            {
                if (buttons is MessageBoxButtons.YesNo or MessageBoxButtons.YesNoCancel)
                {
                    bool? res = dialog.ShowMessageBox(text ?? string.Empty, caption ?? "Skua", true);
                    return res == true ? DialogResult.Yes : DialogResult.No;
                }
                dialog.ShowMessageBox(text ?? string.Empty, caption ?? "Skua");
            }
        }
        catch
        {
        }
        return DialogResult.OK;
    }
}

public static class Clipboard
{
    public static void SetText(string text)
    {
        try
        {
            Ioc.Default.GetService<IClipboardService>()?.SetText(text);
        }
        catch
        {
        }
    }

    public static string GetText()
    {
        try
        {
            return Ioc.Default.GetService<IClipboardService>()?.GetText() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

public delegate void MethodInvoker();

public enum FormStartPosition
{
    Manual = 0,
    CenterScreen = 1,
    WindowsDefaultLocation = 2,
    WindowsDefaultBounds = 3,
    CenterParent = 4
}

public enum FormBorderStyle
{
    None = 0,
    FixedSingle = 1,
    Fixed3D = 2,
    FixedDialog = 3,
    Sizable = 4,
    FixedToolWindow = 5,
    SizableToolWindow = 6
}

public enum DockStyle
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 3,
    Right = 4,
    Fill = 5
}

public enum ProgressBarStyle
{
    Blocks = 0,
    Continuous = 1,
    Marquee = 2
}

public class PaintEventArgs : EventArgs
{
    public object? Graphics { get; }
}

public delegate void PaintEventHandler(object? sender, PaintEventArgs e);

public class Control : IDisposable
{
    public string Text { get; set; } = string.Empty;
    public System.Drawing.Size Size { get; set; }
    public System.Drawing.Point Location { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DockStyle Dock { get; set; }
    public System.Drawing.Color ForeColor { get; set; }
    public System.Drawing.Color BackColor { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public List<Control> Controls { get; } = new();

    public event PaintEventHandler? Paint;
    public event EventHandler? Click;

    public object? Invoke(Delegate method, params object?[]? args)
    {
        return method.DynamicInvoke(args);
    }

    public void BeginInvoke(Delegate method, params object?[]? args)
    {
        method.DynamicInvoke(args);
    }

    public void Show() { }
    public void Hide() { }
    public void Refresh() => Paint?.Invoke(this, new PaintEventArgs());
    public void PerformClick() => Click?.Invoke(this, EventArgs.Empty);
    public virtual void Dispose() { }
}

public class Form : Control
{
    public FormStartPosition StartPosition { get; set; }
    public FormBorderStyle FormBorderStyle { get; set; }
    public bool MaximizeBox { get; set; } = true;
    public bool MinimizeBox { get; set; } = true;
    public bool TopMost { get; set; }
    public DialogResult DialogResult { get; set; }

    public event EventHandler? Shown;
    public event EventHandler? Load;
    public event EventHandler? FormClosed;

    public DialogResult ShowDialog()
    {
        Load?.Invoke(this, EventArgs.Empty);
        Shown?.Invoke(this, EventArgs.Empty);
        return DialogResult;
    }

    public void Close()
    {
        FormClosed?.Invoke(this, EventArgs.Empty);
    }
}

public class ProgressBar : Control
{
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public int Value { get; set; }
    public ProgressBarStyle Style { get; set; }
}

public class Label : Control
{
    public bool AutoSize { get; set; }
}

public class Button : Control
{
    public DialogResult DialogResult { get; set; }
}

public class TextBox : Control
{
    public bool Multiline { get; set; }
    public bool ReadOnly { get; set; }
}

public static class Application
{
    public static void Run(Form mainForm)
    {
        mainForm.ShowDialog();
    }

    public static void DoEvents() { }
    public static void Exit() { }
}
