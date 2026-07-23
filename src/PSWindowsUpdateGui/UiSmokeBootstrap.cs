#if UI_SMOKE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;
using PSWindowsUpdateGui.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PSWindowsUpdateGui;

internal static class UiSmokeBootstrap
{
    public static bool TryConfigure(IList<string> args)
    {
        if (!args.Contains("--ui-smoke", StringComparer.OrdinalIgnoreCase)) return false;
        var engine = new SmokeWindowsUpdateEngine();
        App.EngineFactoryOverride = () => engine;
        App.ThemeOverride = Value(args, "--theme") ?? "System";
        var capturePath = Value(args, "--capture");
        var page = Value(args, "--page") ?? "history";
        App.WindowCreatedForTest = window =>
        {
            foreach (var update in engine.SampleUpdates) window.ViewModel.Updates.Add(update);
            foreach (var history in engine.SampleHistory) window.ViewModel.HistoryEntries.Add(new HistoryItemViewModel(history));
            window.ViewModel.SelectedHistoryEntry = window.ViewModel.HistoryEntries[0];
            window.ShowPage(string.Equals(page, "updates", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
            if (capturePath == null) return;
            var captured = false;
            window.Activated += async (_, __) =>
            {
                if (captured) return;
                captured = true;
                try
                {
                    await Task.Delay(700);
                    await CaptureAsync(window.RootElement, Path.GetFullPath(capturePath));
                }
                catch (Exception exception)
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-smoke-error.log"), exception.ToString());
                }
                finally { window.Close(); }
            };
        };
        return true;
    }

    private static async Task CaptureAsync(FrameworkElement root, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        var pixels = await bitmap.GetPixelsAsync();
        var bytes = new byte[checked((int)pixels.Length)];
        using (var reader = DataReader.FromBuffer(pixels)) reader.ReadBytes(bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (File.Create(path)) { }
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }

    private static string? Value(IList<string> args, string name)
    {
        for (var index = 0; index < args.Count; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count) return args[index + 1];
        return null;
    }
}

internal sealed class SmokeWindowsUpdateEngine : IWindowsUpdateEngine
{
    private readonly IReadOnlyList<UpdateRecord> _updates = new[]
    {
        new UpdateRecord { Identity = UpdateKey.Parse("dac82a19-f9ba-459d-a30a-f56d4b326faa:3"), Title = "Intel - System - 11.7.0.1000", Type = "Driver", DriverProvider = "Intel", DriverVersion = "11.7.0.1000", MaximumDownloadBytes = 12_582_912 },
        new UpdateRecord { Identity = UpdateKey.Parse("8d6a8a0e-f3b4-43e7-b4da-40d63d0ffea9:1"), Title = "2026-07 Cumulative Update for Windows 11 Version 24H2", Type = "Software", KbArticleIds = new[] { "5062553" }, MaximumDownloadBytes = 1_234_567_890 }
    };
    private readonly IReadOnlyList<HistoryRecord> _history = new[]
    {
        new HistoryRecord { DateUtc = new DateTime(2026, 7, 22, 10, 48, 5, DateTimeKind.Utc), Title = "Lenovo System Driver Update (10.2.5.3)", Operation = "uoInstallation", Result = "orcSucceeded", Client = "UpdateOrchestrator", Identity = UpdateKey.Parse("dac82a19-f9ba-459d-a30a-f56d4b326faa:3") },
        new HistoryRecord { DateUtc = new DateTime(2026, 7, 22, 10, 35, 1, DateTimeKind.Utc), Title = "2026-07 Cumulative Update for Windows 11 Version 24H2 (KB5062553)", Operation = "uoInstallation", Result = "orcSucceededWithErrors", HResult = unchecked((int)0x8024200D), Client = "UpdateOrchestrator", Identity = UpdateKey.Parse("8d6a8a0e-f3b4-43e7-b4da-40d63d0ffea9:1") }
    };
    public IReadOnlyList<UpdateRecord> SampleUpdates => _updates;
    public IReadOnlyList<HistoryRecord> SampleHistory => _history;
    public Task<IReadOnlyList<UpdateRecord>> ScanAsync(ScanRequest request, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken) => Task.FromResult(_updates);
    public Task<UpdateActionResult> ExecuteAsync(UpdateActionRequest request, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken) => Task.FromResult(new UpdateActionResult { Action = request.Action, Result = "Planned" });
    public Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(int limit, CancellationToken cancellationToken) => Task.FromResult(_history);
    public Task<UpdateSystemStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new UpdateSystemStatus { ComputerName = "WIN11-LAB", AgentVersion = "WinUI smoke adapter", UpdateServiceStatus = "Running", LocalTime = DateTimeOffset.Now });
    public Task<IReadOnlyList<UpdateServiceRecord>> GetServicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UpdateServiceRecord>>(new[] { new UpdateServiceRecord { Name = "Windows Update", ServiceId = "9482f4b4-e343-43b6-b170-9a65bc822c77" } });
    public Task<string> AddMicrosoftUpdateServiceAsync(bool planOnly, CancellationToken cancellationToken) => Task.FromResult("Planned");
    public Task RemoveServiceAsync(string serviceId, bool planOnly, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ExportPayloadsAsync(IList<UpdateKey> updates, string destination, CancellationToken cancellationToken) => Task.CompletedTask;
    public void Dispose() { }
}
#endif
