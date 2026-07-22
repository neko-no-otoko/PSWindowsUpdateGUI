using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PSWindowsUpdateGui.ViewModels;

namespace PSWindowsUpdateGui.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    internal MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, __) => await _viewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void NavigateAdvanced_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = AdvancedTab;
    }

    private void Credential_Click(object sender, RoutedEventArgs e)
    {
        if (!(sender is Button button) || !(button.DataContext is ParameterInputViewModel input)) return;
        var dialog = new CredentialDialog(input.Name) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            input.Credential = dialog.Credential;
        }
    }

    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export redacted PSWindowsUpdate GUI log",
            Filter = "Text log (*.log)|*.log|Text file (*.txt)|*.txt",
            FileName = $"PSWindowsUpdateGUI-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, _viewModel.LogText);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "PSWindowsUpdate GUI 1.0.0\n\n" +
            "Portable Windows 11 x64 interface for PSWindowsUpdate 2.2.1.5.\n\n" +
            "GUI source: MIT License\n" +
            "PSWindowsUpdate: Copyright Michal Gajda, MIT License\n" +
            "https://github.com/mgajda83/PSWindowsUpdate",
            "About PSWindowsUpdate GUI",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosing(e);
    }
}
