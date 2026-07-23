using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly AppThemeService _themeService;
    private readonly IUserDialogService _dialogs;
    private readonly SynchronizationContext? _uiContext;
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
    private string _selectedTheme = AppThemeService.SystemPreference;
    private string _titleFilter = string.Empty;
    private string _kbFilter = string.Empty;
    private bool _driversOnly;
    private bool _includeHidden;
    private bool _acceptEulas;
    private string _offlineCabPath = string.Empty;
    private string _policyChanges = string.Empty;
    private UpdateSystemStatus _currentStatus = CreateEmptyStatus();
    private HistoryItemViewModel? _selectedHistoryEntry;
    private int _selectedMainTabIndex;
    private string _registeredServicesSummary = "Select Registered services to load the target's update sources.";
    private bool _disposed;

    public MainViewModel(IWindowsUpdateEngine engine, PortableSettingsService settingsService, PortableSettings settings, PortableLogService log, AppThemeService themeService, IUserDialogService dialogs,
        string? identityOverride = null, bool? elevationOverride = null, string? persistenceNoticeOverride = null, string? localComputerOverride = null)
    {
        _engine = engine;
        _settingsService = settingsService;
        _settings = settings;
        _log = log;
        _themeService = themeService;
        _dialogs = dialogs;
        _uiContext = SynchronizationContext.Current;
        _identityOverride = identityOverride;
        _elevationOverride = elevationOverride;
        _persistenceNoticeOverride = persistenceNoticeOverride;
        _localComputerOverride = localComputerOverride;
        _selectedSource = settings.LastUpdateSource;
        _selectedTheme = AppThemeService.NormalizePreference(settings.ThemePreference);
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
        CheckHistoryRemovalCommand = new AsyncRelayCommand(CheckHistoryRemovalAsync, () => !IsBusy && SelectedHistoryEntry?.CanCheckRemoval == true);
        UninstallHistoryCommand = new AsyncRelayCommand(UninstallHistoryAsync, () => !IsBusy && SelectedHistoryEntry?.CanUninstall == true);
        ServicesCommand = new AsyncRelayCommand(ServicesAsync, () => !IsBusy);
        AddMicrosoftUpdateCommand = new AsyncRelayCommand(AddMicrosoftUpdateAsync, () => !IsBusy);
        OfflineScanCommand = new AsyncRelayCommand(OfflineScanAsync, () => !IsBusy);
        DownloadOfflineCatalogCommand = new AsyncRelayCommand(DownloadOfflineCatalogAsync, () => !IsBusy);
        PreviewPolicyCommand = new AsyncRelayCommand(PreviewPolicyAsync);
        ApplyPolicyCommand = new AsyncRelayCommand(ApplyPolicyAsync, () => !IsBusy);
        ResetComponentsCommand = new AsyncRelayCommand(ResetComponentsAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync);
    }

    public ObservableCollection<UpdateRecord> Updates { get; } = new ObservableCollection<UpdateRecord>();
    public ObservableCollection<HistoryItemViewModel> HistoryEntries { get; } = new ObservableCollection<HistoryItemViewModel>();
    public IReadOnlyList<OperationDefinition> Operations => OperationCatalog.Operations;
    public string[] SourceOptions { get; } = { "Default", "Windows Update", "Microsoft Update", "Managed Server" };
    public string[] ThemeOptions { get; } = { AppThemeService.SystemPreference, AppThemeService.LightPreference, AppThemeService.DarkPreference };
    public ICommand ScanCommand { get; }
    public ICommand TestTargetCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand HideCommand { get; }
    public ICommand UnhideCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand HistoryCommand { get; }
    public ICommand CheckHistoryRemovalCommand { get; }
    public ICommand UninstallHistoryCommand { get; }
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
    public string EngineVersion => "Native WUA engine 3.0.0-beta.1";
    public string Identity => _identityOverride ?? WindowsIdentity.GetCurrent().Name;
    public bool IsElevated { get { if (_elevationOverride.HasValue) return _elevationOverride.Value; using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); } }
    public string ElevationDisplay => IsElevated ? "Administrator" : "Not elevated (UI smoke test only)";
    public bool IsEphemeral => _settingsService.IsEphemeral;
    public string PersistenceNotice => _persistenceNoticeOverride ?? (IsEphemeral ? "The EXE directory is not writable. This session is ephemeral." : $"Portable data: {_settingsService.DataDirectory}");

    public bool IsRemote { get => _isRemote; set { if (SetProperty(ref _isRemote, value)) { RaisePropertyChanged(nameof(TargetDisplay)); RaisePropertyChanged(nameof(IsLocal)); ResetTargetSnapshot(); } } }
    public bool IsLocal => !IsRemote;
    public bool UseWinRmHttps { get => _useWinRmHttps; set => SetProperty(ref _useWinRmHttps, value); }
    public string ComputerName { get => _computerName; set { if (SetProperty(ref _computerName, value)) { RaisePropertyChanged(nameof(TargetDisplay)); if (IsRemote) ResetTargetSnapshot(); } } }
    public string TargetDisplay => IsRemote ? (string.IsNullOrWhiteSpace(ComputerName) ? "Remote target" : ComputerName) : (_localComputerOverride ?? Environment.MachineName);
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string OutputText { get => _outputText; private set => SetProperty(ref _outputText, value); }
    public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }
    public UpdateSystemStatus CurrentStatus { get => _currentStatus; private set => SetProperty(ref _currentStatus, value); }
    public HistoryItemViewModel? SelectedHistoryEntry { get => _selectedHistoryEntry; set { if (SetProperty(ref _selectedHistoryEntry, value)) RaiseCommandStates(); } }
    public int SelectedMainTabIndex { get => _selectedMainTabIndex; set { if (SetProperty(ref _selectedMainTabIndex, value)) RaisePropertyChanged(nameof(IsOperationOutputVisible)); } }
    public bool IsOperationOutputVisible => SelectedMainTabIndex != 1;
    public string RegisteredServicesSummary { get => _registeredServicesSummary; private set => SetProperty(ref _registeredServicesSummary, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RaiseCommandStates(); } }
    public int Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string SelectedSource { get => _selectedSource; set { if (SetProperty(ref _selectedSource, value)) { _settings.LastUpdateSource = value; SelectedHistoryEntry?.ResetRemovalVerification("The verification source changed. Check removal again before modifying Windows."); RaiseCommandStates(); } } }
    public string SelectedTheme { get => _selectedTheme; set { var normalized = AppThemeService.NormalizePreference(value); if (SetProperty(ref _selectedTheme, normalized)) { _settings.ThemePreference = normalized; _themeService.Apply(normalized); } } }
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
        if (selected.Count == 0) { await _dialogs.ShowMessageAsync(action.ToString(), "Select at least one update first."); return; }
        var summary = $"{action} {selected.Count} update(s) on {TargetDisplay}? Updates are revalidated by GUID and revision. Automatic reboot is disabled.";
        if (!await _dialogs.ConfirmAsync($"Confirm {action}", summary, action.ToString())) return;
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
            CurrentStatus = IsRemote
                ? await RemoteGuiBridge.StatusAsync(ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
                : await _engine.GetStatusAsync(token).ConfigureAwait(true);
            StatusText = $"Ready — {EngineVersion}, {ElevationDisplay} as {Identity}";
        }).ConfigureAwait(true);
    }

    private async Task HistoryAsync() => await RunAsync("Reading update history", false, async token =>
    {
        var history = IsRemote
            ? await RemoteGuiBridge.HistoryAsync(ComputerName, UseWinRmHttps, 200, token).ConfigureAwait(true)
            : await _engine.GetHistoryAsync(200, token).ConfigureAwait(true);
        SelectedHistoryEntry = null;
        HistoryEntries.Clear();
        foreach (var item in history) HistoryEntries.Add(new HistoryItemViewModel(item));
        StatusText = $"History complete — {history.Count} entries";
    }).ConfigureAwait(true);

    private async Task CheckHistoryRemovalAsync()
    {
        var selected = SelectedHistoryEntry;
        if (selected == null || !selected.CanCheckRemoval) return;
        var sourceName = SelectedSource;
        var source = ParseSource(sourceName);

        await RunAsync("Checking selected update removal capability", false, async token =>
        {
            var request = new UpdateActionRequest
            {
                Action = UpdateActionKind.Uninstall,
                Updates = new List<UpdateKey> { selected.Identity },
                Source = source,
                PlanOnly = true
            };

            try
            {
                var result = IsRemote
                    ? await RemoteGuiBridge.ExecuteAsync(request, ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
                    : await _engine.ExecuteAsync(request, CreateProgress(), token).ConfigureAwait(true);
                if (!string.Equals(result.Result, "Planned", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Windows did not return a removable plan for this update.");
                selected.MarkRemovalVerified(sourceName);
                StatusText = "Removal check complete — exact installed revision is uninstallable";
            }
            catch (Exception exception)
            {
                selected.MarkRemovalUnavailable("This exact installed revision is not removable from the selected source: " + exception.Message);
                throw;
            }
            finally { RaiseCommandStates(); }
        }).ConfigureAwait(true);
    }

    private async Task UninstallHistoryAsync()
    {
        var selected = SelectedHistoryEntry;
        if (selected == null || !selected.CanUninstall) return;
        if (!string.Equals(selected.VerifiedSource, SelectedSource, StringComparison.Ordinal))
        {
            selected.ResetRemovalVerification("The update source changed. Check removal again before modifying Windows.");
            RaiseCommandStates();
            return;
        }
        var source = ParseSource(selected.VerifiedSource);

        var summary = $"Uninstall the currently installed update below from {TargetDisplay}?{Environment.NewLine}{Environment.NewLine}" +
                      $"{selected.Title}{Environment.NewLine}{selected.IdentityDisplay}{Environment.NewLine}{Environment.NewLine}" +
                      "The history entry is an audit record and will remain. A driver rolls back only if Windows has a previous package available. " +
                      "Automatic reboot is disabled.";
        if (!await _dialogs.ConfirmAsync("Confirm uninstall / rollback", summary, "Uninstall")) return;

        await RunAsync("Uninstalling selected history update", true, async token =>
        {
            var request = new UpdateActionRequest
            {
                Action = UpdateActionKind.Uninstall,
                Updates = new List<UpdateKey> { selected.Identity },
                Source = source
            };
            var result = IsRemote
                ? await RemoteGuiBridge.ExecuteAsync(request, ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
                : await _engine.ExecuteAsync(request, CreateProgress(), token).ConfigureAwait(true);
            OutputText = $"{result.Action}: {result.Result}; reboot required: {result.RebootRequired}" + Environment.NewLine +
                         string.Join(Environment.NewLine, result.Updates.Select(item => $"{item.Identity}  {item.Result}  {item.Title}"));
            selected.MarkRemovalCompleted(result.RebootRequired);
            CurrentStatus = IsRemote
                ? await RemoteGuiBridge.StatusAsync(ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
                : await _engine.GetStatusAsync(token).ConfigureAwait(true);
            StatusText = result.RebootRequired ? "Removal complete — restart required" : "Removal complete";
            RaiseCommandStates();
        }).ConfigureAwait(true);
    }

    private async Task ServicesAsync() => await RunAsync("Reading update services", false, async token =>
    {
        var services = IsRemote
            ? await RemoteGuiBridge.ServicesAsync(ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
            : await _engine.GetServicesAsync(token).ConfigureAwait(true);
        RegisteredServicesSummary = services.Count == 0
            ? "Registered services: none reported"
            : "Registered services: " + string.Join("; ", services.Select(item => item.Name));
        OutputText = string.Join(Environment.NewLine, services.Select(item => $"{item.ServiceId}  {item.Name}  Managed={item.IsManaged}  AU={item.IsRegisteredWithAutomaticUpdates}"));
        StatusText = $"Found {services.Count} update service(s)";
    }).ConfigureAwait(true);

    private async Task AddMicrosoftUpdateAsync()
    {
        if (!await _dialogs.ConfirmAsync("Confirm service registration", "Register Microsoft Update with Windows Update Agent?", "Register")) return;
        await RunAsync("Registering Microsoft Update", true, async token => OutputText = IsRemote
            ? await RemoteGuiBridge.AddMicrosoftUpdateServiceAsync(ComputerName, UseWinRmHttps, token).ConfigureAwait(true)
            : await _engine.AddMicrosoftUpdateServiceAsync(false, token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    private async Task OfflineScanAsync()
    {
        if (!await RequireLocalTargetAsync("Offline scan CAB paths are local to this computer. Use the CLI with --computer for a CAB already present on the remote target.")) return;
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
        if (!await RequireLocalTargetAsync("Download the catalog on the remote target with the CLI, or switch to this computer.")) return;
        if (string.IsNullOrWhiteSpace(OfflineCabPath))
            OfflineCabPath = System.IO.Path.Combine(_settingsService.DataDirectory, "wsusscn2.cab");
        await RunAsync("Downloading Microsoft offline scan catalog", false, async token =>
        {
            var result = await new OfflineCatalogService().DownloadAsync(OfflineCabPath, token).ConfigureAwait(true);
            OutputText = $"Downloaded {result.Path}{Environment.NewLine}Size: {result.SizeBytes:N0} bytes{Environment.NewLine}SHA-256: {result.Sha256}{Environment.NewLine}WUA will validate the Microsoft signature when the catalog is registered for scanning.";
        }).ConfigureAwait(true);
    }

    private async Task PreviewPolicyAsync()
    {
        try { OutputText = new WindowsUpdatePolicyService().Preview(SplitLines(PolicyChanges)); }
        catch (Exception exception) { await _dialogs.ShowMessageAsync("Policy validation", exception.Message); }
    }

    private async Task ApplyPolicyAsync()
    {
        var service = new WindowsUpdatePolicyService();
        IList<string> changes;
        try { changes = SplitLines(PolicyChanges); OutputText = service.Preview(changes); }
        catch (Exception exception) { await _dialogs.ShowMessageAsync("Policy validation", exception.Message); return; }
        if (!await _dialogs.ConfirmAsync("Confirm policy changes", OutputText, "Apply policy")) return;
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
        if (!await _dialogs.ConfirmAsync("Confirm component reset", string.Join(Environment.NewLine, preview), "Reset components")) return;
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

    private UpdateSourceKind ParseSource() => ParseSource(SelectedSource);

    private static UpdateSourceKind ParseSource(string source)
    {
        if (source == "Windows Update") return UpdateSourceKind.WindowsUpdate;
        if (source == "Microsoft Update") return UpdateSourceKind.MicrosoftUpdate;
        if (source == "Managed Server") return UpdateSourceKind.ManagedServer;
        return UpdateSourceKind.Default;
    }

    private void ResetTargetSnapshot()
    {
        SelectedHistoryEntry = null;
        HistoryEntries.Clear();
        CurrentStatus = CreateEmptyStatus(TargetDisplay);
        RegisteredServicesSummary = "Select Registered services to load the target's update sources.";
        StatusText = "Target changed — refresh status and history";
    }

    private static UpdateSystemStatus CreateEmptyStatus(string computerName = "Not loaded") => new UpdateSystemStatus
    {
        ComputerName = computerName,
        AgentVersion = string.Empty,
        UpdateServiceStatus = "Not checked",
        OrchestratorNotice = "Refresh status to read the current Windows Update state."
    };

    private void Populate(IEnumerable<UpdateRecord> updates) { Updates.Clear(); foreach (var update in updates) Updates.Add(update); }

    private IProgress<UpdateProgress> CreateProgress() => new Progress<UpdateProgress>(value => { Progress = Math.Max(0, Math.Min(100, value.PercentComplete)); StatusText = value.Activity; });

    private async Task RunAsync(string activity, bool modifying, Func<CancellationToken, Task> operation)
    {
        IsBusy = true; Progress = 0; StatusText = activity + "…"; _cancellation?.Dispose(); _cancellation = new CancellationTokenSource();
        _log.Write("Operation", $"{activity}; target={TargetDisplay}; modifying={modifying}");
        try { await operation(_cancellation.Token).ConfigureAwait(true); if (Progress < 100) Progress = 100; }
        catch (OperationCanceledException) { StatusText = modifying ? "Cancellation requested; verify the final Windows Update state" : "Operation cancelled"; }
        catch (Exception exception) { StatusText = "Operation failed"; OutputText = exception.ToString(); _log.Write("Error", exception.ToString()); await _dialogs.ShowMessageAsync(activity, exception.Message); }
        finally { IsBusy = false; }
    }

    private void Cancel() => _cancellation?.Cancel();
    private async Task ClearLogsAsync() { if (!await _dialogs.ConfirmAsync("Clear logs", "Delete all portable log files?", "Delete logs")) return; _log.Clear(); LogText = string.Empty; }
    private async Task<bool> RequireLocalTargetAsync(string message) { if (!IsRemote) return true; await _dialogs.ShowMessageAsync("Remote target", message); return false; }
    private void OnLogEntry(object? sender, string entry)
    {
        void Append() { var text = new StringBuilder(LogText).AppendLine(entry); if (text.Length > 250000) text.Remove(0, text.Length - 200000); LogText = text.ToString(); }
        if (_uiContext == null) Append(); else _uiContext.Post(_ => Append(), null);
    }
    private void RaiseCommandStates() { foreach (var command in GetType().GetProperties().Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType)).Select(property => property.GetValue(this))) { if (command is RelayCommand relay) relay.RaiseCanExecuteChanged(); if (command is AsyncRelayCommand asyncRelay) asyncRelay.RaiseCanExecuteChanged(); } }
    private static string[] Split(string value) => value.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
    private static IList<string> SplitLines(string value) => value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
    public void SaveSettings() => _settingsService.Save(_settings);
    public void Dispose() { if (_disposed) return; _disposed = true; SaveSettings(); _cancellation?.Cancel(); _cancellation?.Dispose(); _log.EntryWritten -= OnLogEntry; }
}
