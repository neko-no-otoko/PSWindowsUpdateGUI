using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;
using PSWindowsUpdateGui.ViewModels;
using PSWindowsUpdateGui.Views;

namespace PSWindowsUpdateGui;

public partial class App : Application
{
    private IWindowsUpdateEngine? _engine;
    private AppThemeService? _themeService;
    private MainWindow? _window;

    internal static Func<IWindowsUpdateEngine>? EngineFactoryOverride { get; set; }
    internal static string? ThemeOverride { get; set; }
    internal static Action<MainWindow>? WindowCreatedForTest { get; set; }
    internal static int ExitCode { get; private set; }

    public App()
    {
        InitializeComponent();
        DebugSettings.XamlResourceReferenceFailed += (_, args) => TraceXaml("Resource", args.Message);
        DebugSettings.BindingFailed += (_, args) => TraceXaml("Binding", args.Message);
        UnhandledException += (_, args) =>
        {
#if UI_SMOKE
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-smoke-error.log"), args.Exception.ToString()); }
            catch { }
#endif
            NativeDialog.Show("Unhandled PSWindowsUpdate GUI error", args.Exception.ToString());
            args.Handled = true;
        };
    }

    private static void TraceXaml(string category, string message)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "xaml-diagnostics.log"), $"{DateTime.UtcNow:O} {category}: {message}{Environment.NewLine}"); }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var windowsBuild = GetWindowsBuildNumber();
            if (!Environment.Is64BitOperatingSystem || windowsBuild < 22000)
                throw new PlatformNotSupportedException($"PSWindowsUpdate GUI supports Windows 11 x64 (build 22000 or newer). Detected build: {windowsBuild}.");

            var settingsService = new PortableSettingsService();
            var settings = settingsService.Load();
            if (ThemeOverride != null) settings.ThemePreference = AppThemeService.NormalizePreference(ThemeOverride);
            _themeService = new AppThemeService();
            _themeService.Apply(settings.ThemePreference);
            var log = new PortableLogService(settingsService.DataDirectory, settings);
            _engine = EngineFactoryOverride?.Invoke() ?? new WuaWindowsUpdateEngine();
            var dialogs = new WinUiDialogService(() => _window?.Content?.XamlRoot);
#if UI_SMOKE
            var viewModel = new MainViewModel(_engine, settingsService, settings, log, _themeService, dialogs,
                identityOverride: @"CONTOSO\UpdateAdmin", elevationOverride: false,
                persistenceNoticeOverride: @"Portable data: C:\Tools\PSWindowsUpdateGUI.Data", localComputerOverride: "WIN11-LAB");
#else
            var viewModel = new MainViewModel(_engine, settingsService, settings, log, _themeService, dialogs);
#endif
            _window = new MainWindow(viewModel, _themeService, dialogs);
            _window.Closed += (_, __) => Cleanup();
            WindowCreatedForTest?.Invoke(_window);
            _window.Activate();
        }
        catch (Exception exception)
        {
            ExitCode = 1;
#if UI_SMOKE
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-smoke-error.log"), exception.ToString()); }
            catch { }
#endif
            NativeDialog.Show("PSWindowsUpdate GUI could not start", exception.ToString());
            Cleanup();
            Exit();
        }
    }

    private void Cleanup()
    {
        _engine?.Dispose();
        _engine = null;
        _themeService?.Dispose();
        _themeService = null;
    }

    internal static int GetWindowsBuildNumber()
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var currentVersion = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
        var value = currentVersion?.GetValue("CurrentBuildNumber")?.ToString();
        return int.TryParse(value, out var build) ? build : Environment.OSVersion.Version.Build;
    }

    private static class NativeDialog
    {
        public static void Show(string title, string message) => _ = MessageBox(IntPtr.Zero, message, title, 0x00000010);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
        private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
    }
}
