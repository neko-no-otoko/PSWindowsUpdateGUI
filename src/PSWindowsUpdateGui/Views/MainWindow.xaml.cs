using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
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

    private void BrowseOfflineCab_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Offline scan catalog (wsusscn2.cab)|*.cab|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) _viewModel.OfflineCabPath = dialog.FileName;
    }

    private void CliHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Run PSWindowsUpdateGUI.exe help from an elevated PowerShell or Command Prompt. " +
            "Use --output json for automation and --plan before modifying commands.",
            "CLI help", MessageBoxButton.OK, MessageBoxImage.Information);
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
            "PSWindowsUpdate GUI 2.0.0-beta.1\n\n" +
            "Independent portable Windows 11 x64 GUI and CLI built directly on Windows Update Agent.\n\n" +
            "No PSWindowsUpdate module is installed or loaded. MIT License.",
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
