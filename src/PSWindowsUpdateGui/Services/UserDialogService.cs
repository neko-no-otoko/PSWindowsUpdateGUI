using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PSWindowsUpdateGui.Services;

internal interface IUserDialogService
{
    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "Continue");
}

internal sealed class WinUiDialogService : IUserDialogService
{
    private readonly Func<XamlRoot?> _xamlRoot;

    public WinUiDialogService(Func<XamlRoot?> xamlRoot) => _xamlRoot = xamlRoot;

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = Create(title, message);
        dialog.CloseButtonText = "Close";
        await dialog.ShowAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "Continue")
    {
        var dialog = Create(title, message);
        dialog.PrimaryButtonText = primaryButtonText;
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Close;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private ContentDialog Create(string title, string message)
    {
        var root = _xamlRoot() ?? throw new InvalidOperationException("The application window is not ready to show a dialog.");
        return new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 520,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            }
        };
    }
}

internal sealed class NonInteractiveDialogService : IUserDialogService
{
    public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "Continue") => Task.FromResult(false);
}
