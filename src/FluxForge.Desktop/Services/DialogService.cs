using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace FluxForge.Desktop.Services;

public interface IDialogService
{
    string? PickFolder(string title, string? initial);
    string? PickFile(string title, string filter, string? initial = null);
    string? SaveFile(string title, string filter, string fileName);
    Task<bool> ConfirmAsync(string title, string message, string confirmText);
    Task AlertAsync(string title, string message);
}

public sealed class DialogService : IDialogService
{
    public string? PickFolder(string title, string? initial)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initial ?? "" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickFile(string title, string filter, string? initial = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, InitialDirectory = initial ?? "", CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, OverwritePrompt = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = System.Windows.TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
        };
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    public async Task AlertAsync(string title, string message)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = System.Windows.TextWrapping.Wrap, MaxWidth = 460 },
            CloseButtonText = "OK",
        };
        await box.ShowDialogAsync();
    }
}
