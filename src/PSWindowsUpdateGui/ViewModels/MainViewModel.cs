using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PSWindowsUpdateGui.Infrastructure;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.ViewModels;

internal sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IWindowsUpdateEngine _engine;
    private readonly PortableSettingsService _settingsService;
    private readonly PortableSettings _settings;
    private readonly PortableLogService _log;
    private readonly string? _identityOverride;
    private readonly bool? _elevationOverride;
    private readonly string? _persistenceNoticeOverride;
    private readonly string? _localComputerOverride;
    private readonly RemotePreflightService _remotePreflight = new RemotePreflightService();
    private CancellationTokenSource? _cancellation;
    private bool _isRemote;
    private bool _useWinRmHttps;
    private string _computerName = string.Empty;
    private string _statusText = "Starting…";
    private string _outputText = string.Empty;
    private string _logText = string.Empty;
    private bool _isBusy;
    private int _progress;
    private string _selectedSource = "Default";
    private string _titleFilter = string.Empty;
    private string _kbFilter = string.Empty;
    private bool _driversOnly;
    private bool _includeHidden;
    private bool _acceptEulas;
    private string _offlineCabPath = string.Empty;
    private string _policyChanges = string.Empty;
    private bool _disposed;

    public MainViewModel(IWindowsUpdateEngine engine, PortableSettingsService settingsService, PortableSettings settings, PortableLogService log,
        string? identityOverride = null, bool? elevationOverride = null, string? persistenceNoticeOverride = null, string? localComputerOverride = null)
    {
        _engine = engine;
        _settingsService = settingsService;
        _settings = settings;
        _log = log;
        _identityOverride = identityOverride;
        _elevationOverride = elevationOverride;
        _persistenceNoticeOverride = persistenceNoticeOverride;
        _localComputerOverride = localComputerOverride;
        _selectedSource = settings.LastUpdateSource;
        _log.EntryWritten += OnLogEntry;

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        TestTargetCommand = new AsyncRelayCommand(TestTargetAsync, () => !IsBusy);
        InstallCommand = new AsyncRelayCommand(() => RunActionAsync(UpdateActionKind.Install), () => !IsBusy);
        DownloadCommand = new AsyncRelayCommand(() => RunActionAsync(UpdateActionKind.Download), () => !IsBusy);
        HideCommand = new AsyncRelayCommand(() => RunActionAsync(UpdateActionKind.Hide), () => !IsBusy);
        UnhideCommand = new AsyncRelayCommand(() => RunActionAsync(UpdateActionKind.Unhide), () => !IsBusy);
        UninstallCommand = new AsyncRelayCommand(() => RunActionAsync(UpdateActionKind.Uninstall), () => !IsBusy);
        RefreshStatusCommand = new AsyncRelayCommand(RefreshStatusAsync, () => !IsBusy);
        HistoryCommand = new AsyncRelayCommand(HistoryAsync, () => !IsBusy);
        ServicesCommand = new AsyncRelayCommand(ServicesAsync, () => !IsBusy);
        AddMicrosoftUpdateCommand = new AsyncRelayCommand(AddMicrosoftUpdateAsync, () => !IsBusy);
        OfflineScanCommand = new AsyncRelayCommand(OfflineScanAsync, () => !IsBusy);
        DownloadOfflineCatalogCommand = new AsyncRelayCommand(DownloadOfflineCatalogAsync, () => !IsBusy);
        PreviewPolicyCommand = new RelayCommand(PreviewPolicy);
        ApplyPolicyCommand = new AsyncRelayCommand(ApplyPolicyAsync, () => !IsBusy);
        ResetComponentsCommand = new AsyncRelayCommand(ResetComponentsAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ClearLogsCommand = new RelayCommand(ClearLogs);
    }

    public ObservableCollection<UpdateRecord> Updates { get; } = new ObservableCollection<UpdateRecord>();
    public IReadOnlyList<OperationDefinition> Operations => OperationCatalog.Operations;
    public string[] SourceOptions { get; } = { "Default", "Windows Update", "Microsoft Update", "Managed Server" };
    public ICommand ScanCommand { get; }
    public ICommand TestTargetCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand HideCommand { get; }
    public ICommand UnhideCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand HistoryCommand { get; }
    public ICommand ServicesCommand { get; }
    public ICommand AddMicrosoftUpdateCommand { get; }
    public ICommand OfflineScanCommand { get; }
    public ICommand DownloadOfflineCatalogCommand { get; }
    public ICommand PreviewPolicyCommand { get; }
    public ICommand ApplyPolicyCommand { get; }
    public ICommand ResetComponentsCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearLogsCommand { get; }

    public string ProductTitle => "PSWindowsUpdate GUI";
    public string EngineVersion => "Native WUA engine 2.0.0-beta.1";
    public string Identity => _identityOverride ?? WindowsIdentity.GetCurrent().Name;
    public bool IsElevated { get { if (_elevationOverride.HasValue) return _elevationOverride.Value; using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); } }
    public string ElevationDisplay => IsElevated ? "Administrator" : "Not elevated (UI smoke test only)";
    public bool IsEphemeral => _settingsService.IsEphemeral;
    public string PersistenceNotice => _persistenceNoticeOverride ?? (IsEphemeral ? "The EXE directory is not writable. This session is ephemeral." : $"Portable data: {_settingsService.DataDirectory}");

    public bool IsRemote { get => _isRemote; set { if (SetProperty(ref _isRemote, value)) { RaisePropertyChanged(nameof(TargetDisplay)); RaisePropertyChanged(nameof(IsLocal)); } } }
    public bool IsLocal => !IsRemote;
    public bool UseWinRmHttps { get => _useWinRmHttps; set => SetProperty(ref _useWinRmHttps, value); }
    public string ComputerName { get => _computerName; set { if (SetProperty(ref _computerName, value)) RaisePropertyChanged(nameof(TargetDisplay)); } }
    public string TargetDisplay => IsRemote ? (string.IsNullOrWhiteSpace(ComputerName) ? "Remote target" : ComputerName) : (_localComputerOverride ?? Environment.MachineName);
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string OutputText { get => _outputText; private set => SetProperty(ref _outputText, value); }
    public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseCommandStates(); } }
    public int Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string SelectedSource { get => _selectedSource; set { if (SetProperty(ref _selectedSource, value)) _settings.LastUpdateSource = value; } }
    public string TitleFilter { get => _titleFilter; set => SetProperty(ref _titleFilter, value); }
    public string KBFilter { get => _kbFilter; set => SetProperty(ref _kbFilter, value); }
    public bool DriversOnly { get => _driversOnly; set => SetProperty(ref _driversOnly, value); }
    public bool IncludeHidden { get => _includeHidden; set => SetProperty(ref _includeHidden, value); }
    public bool AcceptEulas { get => _acceptEulas; set => SetProperty(ref _acceptEulas, value); }
    public string OfflineCabPath { get => _offlineCabPath; set => SetProperty(ref _offlineCabPath, value); }
    public string PolicyChanges { get => _policyChanges; set => SetProperty(ref _policyChanges, value); }

    public async Task InitializeAsync()
    {
        LogText = _log.ReadCurrent();
        await RefreshStatusAsync().ConfigureAwait(true);
    }

    private async Task ScanAsync()
    {
        var request = BuildScanRequest();
        await RunAsync("Scanning for updates", false, async token =>
        {
            if (IsRemote)
            {
                var result = await RemoteGuiBridge.ScanAsync(request, ComputerName, UseWinRmHttps, token).ConfigureAwait(true);
                Populate(result);
            }
            else Populate(await _engine.ScanAsync(request, CreateProgress(), token).ConfigureAwait(true));
            StatusText = $"Scan complete — {Updates.Count} update(s) found";
            OutputText = string.Join(Environment.NewLine, Updates.Select(update => $"{update.Identity}  {update.Type}  {update.Kb}  {update.Title}"));
        }).ConfigureAwait(true);
    }

    private async Task RunActionAsync(UpdateActionKind action)
    {
        var selected = Updates.Where(update => update.IsSelected).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select at least one update first.", action.ToString(), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var summary = $"{action} {selected.Count} update(s) on {TargetDisplay}? Updates are revalidated by GUID and revision. Automatic reboot is disabled.";
        if (MessageBox.Show(summary, $"Confirm {action}", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var request = new UpdateActionRequest
        {
            Action = action,
            Updates = selected.Select(update => update.Identity).ToList(),
            Source = ParseSource(),
            AcceptEulas = AcceptEulas
        };
        await RunAsync(action + "ing selected updates", true, async token =>
        {
            var result = IsRemote
                ? await RemoteGuiBridge.ExecuteAsync(request, ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
                : await _engine.ExecuteAsync(request, CreateProgress(), token).ConfigureAwait(true);
            OutputText = $"{result.Action}: {result.Result}; reboot required: {result.RebootRequired}" + Environment.NewLine +
                         string.Join(Environment.NewLine, result.Updates.Select(item => $"{item.Identity}  {item.Result}  {item.Title}"));
            StatusText = result.RebootRequired ? "Operation complete — restart required" : "Operation complete";
        }).ConfigureAwait(true);
    }

    private async Task TestTargetAsync()
    {
        if (!IsRemote) { await RefreshStatusAsync().ConfigureAwait(true); return; }
        await RunAsync("Running secure remote preflight", false, token => Task.Run(() =>
        {
            var result = _remotePreflight.Run(ComputerName, UseWinRmHttps);
            OutputText = result.ToString();
            StatusText = result.Succeeded ? $"Connection ready — {ComputerName}" : "Remote preflight failed";
            if (!result.Succeeded) throw new InvalidOperationException("One or more required remote preflight checks failed.");
        }, token)).ConfigureAwait(true);
    }

    private async Task RefreshStatusAsync()
    {
        await RunAsync("Reading Windows Update status", false, async token =>
        {
            var status = IsRemote
                ? await RemoteGuiBridge.StatusAsync(ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
                : await _engine.GetStatusAsync(token).ConfigureAwait(true);
            OutputText = $"Computer: {status.ComputerName}{Environment.NewLine}WUA: {status.AgentVersion}{Environment.NewLine}" +
                         $"Service: {status.UpdateServiceStatus}{Environment.NewLine}Installer busy: {status.InstallerBusy}{Environment.NewLine}" +
                         $"Reboot required: {status.RebootRequired}{Environment.NewLine}Target-local time: {status.LocalTime:O}{Environment.NewLine}{status.OrchestratorNotice}";
            StatusText = $"Ready — {EngineVersion}, {ElevationDisplay} as {Identity}";
        }).ConfigureAwait(true);
    }

    private async Task HistoryAsync() => await RunAsync("Reading update history", false, async token =>
    {
        var history = IsRemote
            ? await RemoteGuiBridge.HistoryAsync(ComputerName, UseWinRmHttps, 200, token).ConfigureAwait(true)
            : await _engine.GetHistoryAsync(200, token).ConfigureAwait(true);
        OutputText = string.Join(Environment.NewLine, history.Select(item => $"{item.DateUtc:u}  {item.Operation,-10} {item.Result,-24} {item.Title}"));
        StatusText = $"History complete — {history.Count} entries";
    }).ConfigureAwait(true);

    private async Task ServicesAsync() => await RunAsync("Reading update services", false, async token =>
    {
        var services = IsRemote
            ? await RemoteGuiBridge.ServicesAsync(ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
            : await _engine.GetServicesAsync(token).ConfigureAwait(true);
        OutputText = string.Join(Environment.NewLine, services.Select(item => $"{item.ServiceId}  {item.Name}  Managed={item.IsManaged}  AU={item.IsRegisteredWithAutomaticUpdates}"));
        StatusText = $"Found {services.Count} update service(s)";
    }).ConfigureAwait(true);

    private async Task AddMicrosoftUpdateAsync()
    {
        if (MessageBox.Show("Register Microsoft Update with Windows Update Agent?", "Confirm service registration", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync("Registering Microsoft Update", true, async token => OutputText = IsRemote
            ? await RemoteGuiBridge.AddMicrosoftUpdateServiceAsync(ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
            : await _engine.AddMicrosoftUpdateServiceAsync(false, token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    private async Task OfflineScanAsync()
    {
        if (!RequireLocalTarget("Offline scan CAB paths are local to this computer. Use the CLI with --computer for a CAB already present on the remote target.")) return;
        var request = BuildScanRequest();
        request.Source = UpdateSourceKind.Offline;
        request.OfflineCabPath = OfflineCabPath;
        await RunAsync("Running offline security scan", false, async token =>
        {
            Populate(await _engine.ScanAsync(request, CreateProgress(), token).ConfigureAwait(true));
            OutputText = $"Offline catalog scan found {Updates.Count} update(s). The CAB contains metadata, not update payloads.";
        }).ConfigureAwait(true);
    }

    private async Task DownloadOfflineCatalogAsync()
    {
        if (!RequireLocalTarget("Download the catalog on the remote target with the CLI, or switch to this computer.")) return;
        if (string.IsNullOrWhiteSpace(OfflineCabPath))
            OfflineCabPath = System.IO.Path.Combine(_settingsService.DataDirectory, "wsusscn2.cab");
        await RunAsync("Downloading Microsoft offline scan catalog", false, async token =>
        {
            var result = await new OfflineCatalogService().DownloadAsync(OfflineCabPath, token).ConfigureAwait(true);
            OutputText = $"Downloaded {result.Path}{Environment.NewLine}Size: {result.SizeBytes:N0} bytes{Environment.NewLine}SHA-256: {result.Sha256}{Environment.NewLine}WUA will validate the Microsoft signature when the catalog is registered for scanning.";
        }).ConfigureAwait(true);
    }

    private void PreviewPolicy()
    {
        try { OutputText = new WindowsUpdatePolicyService().Preview(SplitLines(PolicyChanges)); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "Policy validation", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task ApplyPolicyAsync()
    {
        var service = new WindowsUpdatePolicyService();
        IList<string> changes;
        try { changes = SplitLines(PolicyChanges); OutputText = service.Preview(changes); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "Policy validation", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        if (MessageBox.Show(OutputText, "Confirm policy changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync("Applying Windows Update policy", true, async token =>
        {
            if (IsRemote)
            {
                OutputText = await RemoteGuiBridge.PolicySetAsync(ComputerName, UseWinRmHttps, changes, token).ConfigureAwait(true);
            }
            else
            {
                var backup = await Task.Run(() => service.Backup(System.IO.Path.Combine(_settingsService.DataDirectory, "PolicyBackups")), token).ConfigureAwait(true);
                await Task.Run(() => service.Apply(changes), token).ConfigureAwait(true);
                OutputText = "Policy values updated. Backup: " + backup;
            }
        }).ConfigureAwait(true);
    }

    private async Task ResetComponentsAsync()
    {
        var service = new WindowsUpdateMaintenanceService();
        var preview = await service.ResetAsync(true, CancellationToken.None).ConfigureAwait(true);
        if (MessageBox.Show(string.Join(Environment.NewLine, preview), "Confirm component reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync("Resetting Windows Update components", true, async token => OutputText = IsRemote
            ? await RemoteGuiBridge.ResetComponentsAsync(ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
            : string.Join(Environment.NewLine, await service.ResetAsync(false, token).ConfigureAwait(true))).ConfigureAwait(true);
    }

    private ScanRequest BuildScanRequest()
    {
        var request = new ScanRequest { Source = ParseSource(), Type = DriversOnly ? UpdateKind.Driver : UpdateKind.All, IncludeHidden = IncludeHidden, TitlePattern = TitleFilter };
        foreach (var value in Split(KBFilter)) request.KbArticleIds.Add(value);
        return request;
    }

    private UpdateSourceKind ParseSource()
    {
        if (SelectedSource == "Windows Update") return UpdateSourceKind.WindowsUpdate;
        if (SelectedSource == "Microsoft Update") return UpdateSourceKind.MicrosoftUpdate;
        if (SelectedSource == "Managed Server") return UpdateSourceKind.ManagedServer;
        return UpdateSourceKind.Default;
    }

    private void Populate(IEnumerable<UpdateRecord> updates) { Updates.Clear(); foreach (var update in updates) Updates.Add(update); }

    private IProgress<UpdateProgress> CreateProgress() => new Progress<UpdateProgress>(value => { Progress = Math.Max(0, Math.Min(100, value.PercentComplete)); StatusText = value.Activity; });

    private async Task RunAsync(string activity, bool modifying, Func<CancellationToken, Task> operation)
    {
        IsBusy = true; Progress = 0; StatusText = activity + "…"; _cancellation?.Dispose(); _cancellation = new CancellationTokenSource();
        _log.Write("Operation", $"{activity}; target={TargetDisplay}; modifying={modifying}");
        try { await operation(_cancellation.Token).ConfigureAwait(true); if (Progress < 100) Progress = 100; }
        catch (OperationCanceledException) { StatusText = modifying ? "Cancellation requested; verify the final Windows Update state" : "Operation cancelled"; }
        catch (Exception exception) { StatusText = "Operation failed"; OutputText = exception.ToString(); _log.Write("Error", exception.ToString()); MessageBox.Show(exception.Message, activity, MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    private void Cancel() => _cancellation?.Cancel();
    private void ClearLogs() { if (MessageBox.Show("Delete all portable log files?", "Clear logs", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; _log.Clear(); LogText = string.Empty; }
    private bool RequireLocalTarget(string message) { if (!IsRemote) return true; MessageBox.Show(message, "Remote target", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
    private void OnLogEntry(object? sender, string entry) => Application.Current.Dispatcher.BeginInvoke(new Action(() => { var text = new StringBuilder(LogText).AppendLine(entry); if (text.Length > 250000) text.Remove(0, text.Length - 200000); LogText = text.ToString(); }));
    private void RaiseCommandStates() { foreach (var command in GetType().GetProperties().Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType)).Select(property => property.GetValue(this))) { if (command is RelayCommand relay) relay.RaiseCanExecuteChanged(); if (command is AsyncRelayCommand asyncRelay) asyncRelay.RaiseCanExecuteChanged(); } }
    private static string[] Split(string value) => value.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
    private static IList<string> SplitLines(string value) => value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
    public void SaveSettings() => _settingsService.Save(_settings);
    public void Dispose() { if (_disposed) return; _disposed = true; SaveSettings(); _cancellation?.Cancel(); _cancellation?.Dispose(); _log.EntryWritten -= OnLogEntry; }
}
