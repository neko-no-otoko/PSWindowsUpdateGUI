using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;
using PSWindowsUpdateGui.ViewModels;
using PSWindowsUpdateGui.Views;

namespace PSWindowsUpdateGui.UiSmoke;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var errorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-smoke-error.log");
        try { if (File.Exists(errorPath)) File.Delete(errorPath); } catch { }
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        application.DispatcherUnhandledException += (_, args) =>
        {
            File.WriteAllText(errorPath, args.Exception.ToString());
            args.Handled = true;
            application.Shutdown(1);
        };
        ConfigureResources(application);
        var settingsService = new PortableSettingsService();
        var settings = settingsService.Load();
        var engine = new FakeWindowsUpdateEngine();
        var log = new PortableLogService(settingsService.DataDirectory, settings);
        var viewModel = new MainViewModel(engine, settingsService, settings, log,
            @"CONTOSO\Admin", true, @"Portable data: .\PSWindowsUpdateGUI.Data (sample)", "WIN11-LAB");
        foreach (var update in engine.SampleUpdates) viewModel.Updates.Add(update);
        var window = new MainWindow(viewModel);
        var capturePath = ReadCapturePath(args);
        if (capturePath != null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = -32000;
            window.ShowInTaskbar = false;
            window.ContentRendered += (_, __) =>
            {
                window.UpdateLayout();
                var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                image.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                Directory.CreateDirectory(Path.GetDirectoryName(capturePath)!);
                using (var stream = File.Create(capturePath)) encoder.Save(stream);
                window.Close();
            };
        }
        var result = application.Run(window);
        engine.Dispose();
        return result;
    }

    private static string? ReadCapturePath(IList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], "--capture", StringComparison.OrdinalIgnoreCase)) continue;
            if (index + 1 >= args.Count) throw new ArgumentException("--capture requires a PNG path.");
            var path = Path.GetFullPath(args[index + 1]);
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The capture path must end in .png.");
            return path;
        }
        return null;
    }

    private static void ConfigureResources(Application application)
    {
        application.Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 6, 12, 6)));
        buttonStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(3)));
        buttonStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30d));
        application.Resources.Add(typeof(Button), buttonStyle);
    }
}

internal sealed class FakeWindowsUpdateEngine : IWindowsUpdateEngine
{
    private readonly IReadOnlyList<UpdateRecord> _updates = new[]
    {
        new UpdateRecord
        {
            Identity = UpdateKey.Parse("dac82a19-f9ba-459d-a30a-f56d4b326faa:3"),
            Title = "Intel - System - 11.7.0.1000",
            Type = "Driver",
            DriverProvider = "Intel",
            DriverClass = "OtherHardware",
            DriverModel = "Intel(R) Watchdog Timer Driver",
            DriverVersion = "11.7.0.1000",
            MaximumDownloadBytes = 12_582_912
        },
        new UpdateRecord
        {
            Identity = UpdateKey.Parse("8d6a8a0e-f3b4-43e7-b4da-40d63d0ffea9:1"),
            Title = "2026-07 Cumulative Update for Windows 11 Version 24H2",
            Type = "Software",
            KbArticleIds = new[] { "5062553" },
            MaximumDownloadBytes = 1_234_567_890
        }
    };

    public IReadOnlyList<UpdateRecord> SampleUpdates => _updates;

    public Task<IReadOnlyList<UpdateRecord>> ScanAsync(ScanRequest request, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken) => Task.FromResult(_updates);
    public Task<UpdateActionResult> ExecuteAsync(UpdateActionRequest request, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken) => Task.FromResult(new UpdateActionResult { Action = request.Action, Result = "Planned" });
    public Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HistoryRecord>>(Array.Empty<HistoryRecord>());
    public Task<UpdateSystemStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new UpdateSystemStatus { ComputerName = "WIN11-LAB", AgentVersion = "UI smoke adapter", UpdateServiceStatus = "Running", LocalTime = DateTimeOffset.Now, OrchestratorNotice = "UI smoke test: no machine state will be changed." });
    public Task<IReadOnlyList<UpdateServiceRecord>> GetServicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UpdateServiceRecord>>(new[] { new UpdateServiceRecord { Name = "Windows Update", ServiceId = "9482f4b4-e343-43b6-b170-9a65bc822c77" } });
    public Task<string> AddMicrosoftUpdateServiceAsync(bool planOnly, CancellationToken cancellationToken) => Task.FromResult("Planned");
    public Task RemoveServiceAsync(string serviceId, bool planOnly, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ExportPayloadsAsync(IList<UpdateKey> updates, string destination, CancellationToken cancellationToken) => Task.CompletedTask;
    public void Dispose() { }
}
