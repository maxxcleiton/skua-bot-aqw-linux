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
}
