using System;
using System.Windows;
using Microsoft.Win32;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;
using PSWindowsUpdateGui.ViewModels;
using PSWindowsUpdateGui.Views;

namespace PSWindowsUpdateGui;

public partial class App : Application
{
    private IWindowsUpdateEngine? _engine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "Unhandled PSWindowsUpdate GUI error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var windowsBuild = GetWindowsBuildNumber();
            if (!Environment.Is64BitOperatingSystem || windowsBuild < 22000)
            {
                throw new PlatformNotSupportedException(
                    $"PSWindowsUpdate GUI supports Windows 11 x64 (build 22000 or newer). Detected build: {windowsBuild}.");
            }

            var settingsService = new PortableSettingsService();
            var settings = settingsService.Load();
            var log = new PortableLogService(settingsService.DataDirectory, settings);
            _engine = new WuaWindowsUpdateEngine();
            var viewModel = new MainViewModel(_engine, settingsService, settings, log);
            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.ToString(), "PSWindowsUpdate GUI could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Dispose();
        base.OnExit(e);
    }

    internal static int GetWindowsBuildNumber()
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var currentVersion = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
        var value = currentVersion?.GetValue("CurrentBuildNumber")?.ToString();
        return int.TryParse(value, out var build) ? build : Environment.OSVersion.Version.Build;
    }
}
