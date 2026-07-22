using System;
using System.Windows;
using PSWindowsUpdateGui.Services;
using PSWindowsUpdateGui.ViewModels;
using PSWindowsUpdateGui.Views;

namespace PSWindowsUpdateGui;

public partial class App : Application
{
    private ModuleRuntime? _module;
    private PowerShellHost? _host;

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
            if (Environment.OSVersion.Version.Build < 22000)
            {
                throw new PlatformNotSupportedException("PSWindowsUpdate GUI supports Windows 11 x64 (build 22000 or newer).");
            }

            var settingsService = new PortableSettingsService();
            var settings = settingsService.Load();
            var log = new PortableLogService(settingsService.DataDirectory, settings);
            _module = ModuleRuntime.Create();
            _host = new PowerShellHost(_module, log);
            var viewModel = new MainViewModel(_host, _module, settingsService, settings, log);
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
        _host?.Dispose();
        _module?.Dispose();
        base.OnExit(e);
    }
}
