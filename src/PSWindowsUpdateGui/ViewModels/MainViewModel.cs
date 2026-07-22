using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
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
    private static readonly ISet<string> HighImpactCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Add-WUServiceManager", "Enable-WURemoting", "Invoke-WUJob", "Remove-WindowsUpdate",
        "Remove-WUServiceManager", "Reset-WUComponents", "Set-PSWUSettings", "Set-WUSettings", "Update-WUModule"
    };

    private readonly PowerShellHost _host;
    private readonly ModuleRuntime _module;
    private readonly PortableSettingsService _settingsService;
    private readonly PortableSettings _settings;
    private readonly PortableLogService _log;
    private readonly RemoteModuleStager _remoteStager;
    private readonly RemotePreflightService _remotePreflight = new RemotePreflightService();
    private CancellationTokenSource? _operationCancellation;
    private RemoteStageResult? _pendingRemoteStage;
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
    private CommandDefinition? _selectedCommand;
    private ParameterSetDefinition? _selectedParameterSet;
    private bool _advancedVerbose;
    private bool _advancedWhatIf;
    private string _commandPreview = string.Empty;
    private bool _disposed;

    public MainViewModel(
        PowerShellHost host,
        ModuleRuntime module,
        PortableSettingsService settingsService,
        PortableSettings settings,
        PortableLogService log)
    {
        _host = host;
        _module = module;
        _settingsService = settingsService;
        _settings = settings;
        _log = log;
        _remoteStager = new RemoteModuleStager(module, log);
        _selectedSource = settings.LastUpdateSource;
        _host.EventReceived += OnInvocationEvent;
        _log.EntryWritten += OnLogEntry;

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        TestTargetCommand = new AsyncRelayCommand(TestTargetAsync, () => !IsBusy);
        InstallCommand = new AsyncRelayCommand(() => RunUpdateActionAsync("Install"), () => !IsBusy);
        DownloadCommand = new AsyncRelayCommand(() => RunUpdateActionAsync("Download"), () => !IsBusy);
        HideCommand = new AsyncRelayCommand(() => RunUpdateActionAsync("Hide"), () => !IsBusy);
        UnhideCommand = new AsyncRelayCommand(() => RunUpdateActionAsync("Unhide"), () => !IsBusy);
        RunAdvancedCommand = new AsyncRelayCommand(RunAdvancedAsync, () => !IsBusy && SelectedCommand != null);
        RefreshPreviewCommand = new RelayCommand(RefreshPreview);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        QuickCommand = new ParameterizedAsyncCommand(parameter => RunQuickAsync(parameter?.ToString() ?? string.Empty), _ => !IsBusy);
        SelectAdvancedCommand = new RelayCommand(parameter => SelectAdvanced(parameter?.ToString() ?? string.Empty));
        ClearLogsCommand = new RelayCommand(ClearLogs);
        CleanupRemoteModuleCommand = new AsyncRelayCommand(CleanupRemoteModuleAsync, () => !IsBusy && _pendingRemoteStage?.WasCreated == true);
    }

    public ObservableCollection<UpdateRow> Updates { get; } = new ObservableCollection<UpdateRow>();

    public ObservableCollection<CommandDefinition> Commands { get; } = new ObservableCollection<CommandDefinition>();

    public ObservableCollection<ParameterInputViewModel> AdvancedInputs { get; } = new ObservableCollection<ParameterInputViewModel>();

    public string[] SourceOptions { get; } = { "Default", "Windows Update", "Microsoft Update", "Service ID" };

    public ICommand ScanCommand { get; }
    public ICommand TestTargetCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand HideCommand { get; }
    public ICommand UnhideCommand { get; }
    public ICommand RunAdvancedCommand { get; }
    public ICommand RefreshPreviewCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand QuickCommand { get; }
    public ICommand SelectAdvancedCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand CleanupRemoteModuleCommand { get; }

    public string ProductTitle => "PSWindowsUpdate GUI";

    public string ModuleVersion => _module.Manifest.PackageVersion;

    public string Identity => WindowsIdentity.GetCurrent().Name;

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public bool IsEphemeral => _settingsService.IsEphemeral;

    public string PersistenceNotice => IsEphemeral
        ? "The EXE directory is not writable. This session is ephemeral."
        : $"Portable data: {_settingsService.DataDirectory}";

    public bool IsRemote
    {
        get => _isRemote;
        set
        {
            if (SetProperty(ref _isRemote, value))
            {
                RaisePropertyChanged(nameof(TargetDisplay));
                RaisePropertyChanged(nameof(RemoteCompatibility));
                RefreshPreview();
            }
        }
    }

    public string ComputerName
    {
        get => _computerName;
        set
        {
            if (SetProperty(ref _computerName, value))
            {
                RaisePropertyChanged(nameof(TargetDisplay));
                RefreshPreview();
            }
        }
    }

    public bool UseWinRmHttps
    {
        get => _useWinRmHttps;
        set => SetProperty(ref _useWinRmHttps, value);
    }

    public string TargetDisplay => IsRemote ? (string.IsNullOrWhiteSpace(ComputerName) ? "Remote target" : ComputerName) : "This computer";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string OutputText
    {
        get => _outputText;
        private set => SetProperty(ref _outputText, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(IsIdle));
                RaiseCommandStates();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public int Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value))
            {
                _settings.LastUpdateSource = value;
            }
        }
    }

    public string TitleFilter
    {
        get => _titleFilter;
        set => SetProperty(ref _titleFilter, value);
    }

    public string KBFilter
    {
        get => _kbFilter;
        set => SetProperty(ref _kbFilter, value);
    }

    public bool DriversOnly
    {
        get => _driversOnly;
        set => SetProperty(ref _driversOnly, value);
    }

    public bool IncludeHidden
    {
        get => _includeHidden;
        set => SetProperty(ref _includeHidden, value);
    }

    public CommandDefinition? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (SetProperty(ref _selectedCommand, value))
            {
                RaisePropertyChanged(nameof(ParameterSets));
                SelectedParameterSet = value?.ParameterSets.FirstOrDefault(set => set.IsDefault) ?? value?.ParameterSets.FirstOrDefault();
                RaisePropertyChanged(nameof(RemoteCompatibility));
            }
        }
    }

    public IEnumerable<ParameterSetDefinition> ParameterSets => SelectedCommand?.ParameterSets ?? Enumerable.Empty<ParameterSetDefinition>();

    public ParameterSetDefinition? SelectedParameterSet
    {
        get => _selectedParameterSet;
        set
        {
            if (SetProperty(ref _selectedParameterSet, value))
            {
                RebuildAdvancedInputs();
                RaisePropertyChanged(nameof(RemoteCompatibility));
            }
        }
    }

    public bool AdvancedVerbose
    {
        get => _advancedVerbose;
        set
        {
            if (SetProperty(ref _advancedVerbose, value)) RefreshPreview();
        }
    }

    public bool AdvancedWhatIf
    {
        get => _advancedWhatIf;
        set
        {
            if (SetProperty(ref _advancedWhatIf, value)) RefreshPreview();
        }
    }

    public string CommandPreview
    {
        get => _commandPreview;
        private set => SetProperty(ref _commandPreview, value);
    }

    public string RemoteCompatibility
    {
        get
        {
            if (!IsRemote || SelectedParameterSet == null) return string.Empty;
            return SelectedParameterSet.Parameters.Any(parameter => parameter.Name == "ComputerName")
                ? "This parameter set supports the selected remote target."
                : "This upstream parameter set has no -ComputerName parameter and is disabled for remote targets.";
        }
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await _host.InitializeAsync().ConfigureAwait(true);
            var catalog = await _host.LoadCatalogAsync().ConfigureAwait(true);
            foreach (var command in catalog)
            {
                Commands.Add(command);
            }

            SelectedCommand = Commands.FirstOrDefault(command => command.Name == "Get-WindowsUpdate");
            LogText = _log.ReadCurrent();
            StatusText = $"Ready — PSWindowsUpdate {ModuleVersion}, Windows PowerShell 5.1, elevated as {Identity}";
        }
        catch (Exception exception)
        {
            StatusText = "Startup verification failed";
            OutputText = exception.ToString();
            _log.Write("Fatal", exception.ToString());
            MessageBox.Show(exception.Message, "PSWindowsUpdate GUI startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanAsync()
    {
        var parameters = CreateTargetParameters("Get-WindowsUpdate");
        ApplySource(parameters);
        if (!string.IsNullOrWhiteSpace(TitleFilter)) parameters["Title"] = TitleFilter;
        if (!string.IsNullOrWhiteSpace(KBFilter)) parameters["KBArticleID"] = Split(KBFilter);
        if (DriversOnly) parameters["UpdateType"] = "Driver";
        if (IncludeHidden) parameters["WithHidden"] = new SwitchParameter(true);

        var result = await ExecuteAsync("Get-WindowsUpdate", parameters, false, "Scanning for updates").ConfigureAwait(true);
        if (result == null) return;
        Updates.Clear();
        foreach (var item in result.Output)
        {
            var update = UpdateRow.From(item);
            if (!string.IsNullOrWhiteSpace(update.Title) || !string.IsNullOrWhiteSpace(update.KB))
            {
                Updates.Add(update);
            }
        }

        StatusText = result.Succeeded ? $"Scan complete — {Updates.Count} update(s) found" : "Scan failed";
    }

    private async Task RunUpdateActionAsync(string action)
    {
        var selected = Updates.Where(update => update.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select at least one update first.", action, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var summary = action == "Install"
            ? $"Install {selected.Count} selected update(s) on {TargetDisplay}? Automatic reboot is disabled."
            : $"{action} {selected.Count} selected update(s) on {TargetDisplay}?";
        if (MessageBox.Show(summary, $"Confirm {action}", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (IsRemote && (action == "Install" || action == "Download"))
        {
            if (MessageBox.Show(
                    "Native remote download/install requires PSWindowsUpdate on the target. If it is absent, the pinned module will be placed temporarily in the remote Windows PowerShell module path.",
                    "Temporary remote module", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            {
                return;
            }

            _pendingRemoteStage = await Task.Run(() => _remoteStager.EnsureAvailable(ComputerName)).ConfigureAwait(true);
            RaiseCommandStates();
        }

        var parameters = CreateTargetParameters("Get-WindowsUpdate");
        ApplySource(parameters);
        var ids = selected.Where(update => !string.IsNullOrWhiteSpace(update.UpdateId)).Select(update => update.UpdateId).ToArray();
        if (ids.Length == selected.Count)
        {
            parameters["UpdateID"] = ids;
        }
        else
        {
            var knowledgeBase = selected.SelectMany(update => Split(update.KB)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (knowledgeBase.Length == 0)
            {
                MessageBox.Show("The selected rows do not contain usable update IDs or KB article IDs.", action, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            parameters["KBArticleID"] = knowledgeBase;
        }

        if (action == "Install")
        {
            parameters["Install"] = new SwitchParameter(true);
            parameters["AcceptAll"] = new SwitchParameter(true);
            parameters["IgnoreReboot"] = new SwitchParameter(true);
        }
        else if (action == "Download")
        {
            parameters["Download"] = new SwitchParameter(true);
            parameters["AcceptAll"] = new SwitchParameter(true);
        }
        else
        {
            parameters["Hide"] = new SwitchParameter(action == "Hide");
            parameters["AcceptAll"] = new SwitchParameter(true);
        }

        var result = await ExecuteAsync("Get-WindowsUpdate", parameters, true, action + "ing selected updates").ConfigureAwait(true);
        if (result != null)
        {
            OutputText = FormatOutput(result);
        }
    }

    private async Task TestTargetAsync()
    {
        if (IsRemote)
        {
            RemoteModuleStager.ValidateComputerName(ComputerName);
            IsBusy = true;
            StatusText = $"Running secure preflight for {ComputerName}â€¦";
            try
            {
                var preflight = await Task.Run(() => _remotePreflight.Run(ComputerName, UseWinRmHttps)).ConfigureAwait(true);
                OutputText = preflight.ToString();
                _log.Write("Preflight", $"{ComputerName}: {preflight.Succeeded}; {preflight}");
                if (!preflight.Succeeded)
                {
                    StatusText = "Remote preflight failed";
                    return;
                }
            }
            catch (Exception exception)
            {
                StatusText = "Remote preflight failed";
                OutputText = exception.ToString();
                return;
            }
            finally
            {
                IsBusy = false;
            }
        }

        var parameters = CreateTargetParameters("Get-WUApiVersion");
        var result = await ExecuteAsync("Get-WUApiVersion", parameters, false, $"Testing {TargetDisplay}").ConfigureAwait(true);
        if (result?.Succeeded == true)
        {
            StatusText = $"Connection ready — {TargetDisplay}";
            if (IsRemote && !_settings.RecentComputers.Contains(ComputerName, StringComparer.OrdinalIgnoreCase))
            {
                _settings.RecentComputers.Insert(0, ComputerName);
                _settings.RecentComputers = _settings.RecentComputers.Take(10).ToList();
            }
        }
    }

    private async Task RunQuickAsync(string command)
    {
        if (!CommandRegistry.IsAllowed(command)) return;
        var definition = Commands.First(item => item.Name == command);
        if (IsRemote && !definition.SupportsRemote)
        {
            MessageBox.Show($"{command} does not expose -ComputerName in the upstream module.", "Remote command unavailable",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (HighImpactCommands.Contains(command))
        {
            SelectAdvanced(command);
            StatusText = $"Configure {command} in the Advanced tab before running it.";
            return;
        }

        var parameters = CreateTargetParameters(command);
        if (string.Equals(command, "Get-WURebootStatus", StringComparison.OrdinalIgnoreCase))
        {
            parameters["Silent"] = new SwitchParameter(true);
        }

        var result = await ExecuteAsync(command, parameters, false, $"Running {command}").ConfigureAwait(true);
        if (result != null) OutputText = FormatOutput(result);
    }

    private async Task RunAdvancedAsync()
    {
        if (SelectedCommand == null || SelectedParameterSet == null) return;
        if (IsRemote && !SelectedParameterSet.Parameters.Any(parameter => parameter.Name == "ComputerName"))
        {
            MessageBox.Show(RemoteCompatibility, "Remote command unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Dictionary<string, object?> parameters;
        try
        {
            parameters = BuildAdvancedParameters();
            ValidateDependencies(parameters);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Parameter validation", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var highImpact = HighImpactCommands.Contains(SelectedCommand.Name) ||
                         parameters.Keys.Any(key => key == "Install" || key == "Download" || key == "Hide" || key == "AutoReboot");
        if (highImpact)
        {
            var warning = SelectedCommand.Name == "Invoke-WUJob" && parameters.ContainsKey("Script")
                ? "This operation executes the supplied PowerShell script with administrator privileges. Review the redacted preview carefully."
                : "This operation can modify Windows Update, system policy, services, credentials, scheduled tasks, or reboot behavior.";
            if (MessageBox.Show(warning + Environment.NewLine + Environment.NewLine + _host.RenderPreview(SelectedCommand.Name, parameters),
                    "Confirm advanced operation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (IsRemote && SelectedCommand.Name == "Get-WindowsUpdate" &&
            (parameters.ContainsKey("Install") || parameters.ContainsKey("Download")))
        {
            _pendingRemoteStage = await Task.Run(() => _remoteStager.EnsureAvailable(ComputerName)).ConfigureAwait(true);
        }

        var result = await ExecuteAsync(SelectedCommand.Name, parameters, highImpact, $"Running {SelectedCommand.Name}").ConfigureAwait(true);
        if (result != null) OutputText = FormatOutput(result);
    }

    private Dictionary<string, object?> BuildAdvancedParameters()
    {
        if (SelectedCommand == null) throw new InvalidOperationException("Select a command.");
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in AdvancedInputs.Where(input => input.IsBound))
        {
            parameters[input.Name] = input.ConvertValue();
        }

        if (IsRemote) parameters["ComputerName"] = new[] { ComputerName };
        if (AdvancedVerbose) parameters["Verbose"] = new SwitchParameter(true);
        if (AdvancedWhatIf && SelectedCommand.SupportsShouldProcess) parameters["WhatIf"] = new SwitchParameter(true);
        if (SelectedCommand.SupportsShouldProcess) parameters["Confirm"] = false;
        return parameters;
    }

    private static void ValidateDependencies(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.ContainsKey("ForceInstall") && !parameters.ContainsKey("Install"))
            throw new FormatException("-ForceInstall requires -Install.");
        if (parameters.ContainsKey("ForceDownload") && !parameters.ContainsKey("Download"))
            throw new FormatException("-ForceDownload requires -Download.");
        var rebootModes = new[] { "AutoReboot", "IgnoreReboot", "ScheduleReboot" }.Count(parameters.ContainsKey);
        if (rebootModes > 1) throw new FormatException("Auto, ignored, and scheduled reboot modes are mutually exclusive.");
        if (parameters.TryGetValue("MinSize", out var minimum) && parameters.TryGetValue("MaxSize", out var maximum) &&
            Convert.ToInt64(minimum, CultureInfo.InvariantCulture) > Convert.ToInt64(maximum, CultureInfo.InvariantCulture))
            throw new FormatException("-MinSize cannot exceed -MaxSize.");
    }

    private void RefreshPreview()
    {
        try
        {
            CommandPreview = SelectedCommand == null ? string.Empty : _host.RenderPreview(SelectedCommand.Name, BuildAdvancedParameters());
        }
        catch (Exception exception)
        {
            CommandPreview = exception.Message;
        }
    }

    private void RebuildAdvancedInputs()
    {
        AdvancedInputs.Clear();
        if (SelectedParameterSet != null)
        {
            foreach (var parameter in SelectedParameterSet.Parameters.Where(parameter => parameter.Name != "ComputerName"))
            {
                var input = new ParameterInputViewModel(parameter);
                input.PropertyChanged += AdvancedInputChanged;
                AdvancedInputs.Add(input);
            }
        }

        RefreshPreview();
    }

    private void AdvancedInputChanged(object? sender, PropertyChangedEventArgs e) => RefreshPreview();

    private void SelectAdvanced(string command)
    {
        var selected = Commands.FirstOrDefault(item => string.Equals(item.Name, command, StringComparison.OrdinalIgnoreCase));
        if (selected != null) SelectedCommand = selected;
    }

    private Dictionary<string, object?> CreateTargetParameters(string command)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!IsRemote) return parameters;
        RemoteModuleStager.ValidateComputerName(ComputerName);
        var definition = Commands.FirstOrDefault(item => item.Name == command);
        if (definition?.SupportsRemote != true)
            throw new InvalidOperationException($"{command} does not natively support -ComputerName.");
        parameters["ComputerName"] = new[] { ComputerName };
        return parameters;
    }

    private void ApplySource(IDictionary<string, object?> parameters)
    {
        if (SelectedSource == "Windows Update") parameters["WindowsUpdate"] = new SwitchParameter(true);
        else if (SelectedSource == "Microsoft Update") parameters["MicrosoftUpdate"] = new SwitchParameter(true);
        else if (SelectedSource == "Service ID")
            throw new InvalidOperationException("Enter a ServiceID through the Advanced Get-WindowsUpdate form.");
    }

    private async Task<InvocationResult?> ExecuteAsync(
        string command,
        IReadOnlyDictionary<string, object?> parameters,
        bool modifying,
        string activity)
    {
        IsBusy = true;
        Progress = 0;
        StatusText = activity + "…";
        OutputText = string.Empty;
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _log.Write("Command", _host.RenderPreview(command, parameters));
        try
        {
            var result = await _host.InvokeAsync(command, parameters, _operationCancellation.Token).ConfigureAwait(true);
            OutputText = FormatOutput(result);
            StatusText = result.WasCancelled
                ? (modifying ? "Monitoring stopped; the target operation may still be running" : "Operation cancelled")
                : result.Succeeded ? "Operation completed" : "Operation failed";
            Progress = result.Succeeded ? 100 : 0;
            return result;
        }
        catch (OperationCanceledException)
        {
            StatusText = modifying ? "Monitoring stopped; the target operation may still be running" : "Operation cancelled";
            return null;
        }
        catch (Exception exception)
        {
            StatusText = "Operation failed";
            OutputText = exception.ToString();
            _log.Write("Error", exception.ToString());
            MessageBox.Show(exception.Message, command, MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CleanupRemoteModuleAsync()
    {
        if (_pendingRemoteStage?.WasCreated != true || !IsRemote) return;
        var jobs = await ExecuteAsync("Get-WUJob", CreateTargetParameters("Get-WUJob"), false, "Checking remote jobs").ConfigureAwait(true);
        if (jobs == null) return;
        if (jobs.Output.Count > 0 && MessageBox.Show(
                "PSWindowsUpdate scheduled jobs are still registered. Remove the temporary module anyway only if those jobs have finished.",
                "Remote cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (_remoteStager.TryRemoveOwned(ComputerName, _pendingRemoteStage.OwnershipToken))
        {
            _log.Write("Remote", $"Removed app-owned temporary module from {ComputerName}.");
            _pendingRemoteStage = null;
            RaiseCommandStates();
        }
    }

    private void Cancel()
    {
        _operationCancellation?.Cancel();
        _host.Stop();
    }

    private void ClearLogs()
    {
        if (MessageBox.Show("Delete all portable log files?", "Clear logs", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _log.Clear();
        LogText = string.Empty;
    }

    private void OnInvocationEvent(object? sender, InvocationEvent e)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.PercentComplete.HasValue) Progress = Math.Max(0, Math.Min(100, e.PercentComplete.Value));
            if (e.Kind == InvocationEventKind.Progress) StatusText = e.Message;
        }));
    }

    private void OnLogEntry(object? sender, string entry)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            var builder = new StringBuilder(LogText);
            builder.AppendLine(entry);
            if (builder.Length > 250_000) builder.Remove(0, builder.Length - 200_000);
            LogText = builder.ToString();
        }));
    }

    private static string FormatOutput(InvocationResult result)
    {
        var builder = new StringBuilder();
        foreach (var output in result.Output) builder.AppendLine(output.ToString());
        foreach (var error in result.Errors) builder.AppendLine("ERROR: " + error);
        if (result.WasCancelled) builder.AppendLine("Monitoring was cancelled.");
        return builder.ToString().Trim();
    }

    private static string[] Split(string value) => value
        .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(item => item.Trim())
        .Where(item => item.Length > 0)
        .ToArray();

    private void RaiseCommandStates()
    {
        foreach (var command in new[] { ScanCommand, TestTargetCommand, InstallCommand, DownloadCommand, HideCommand,
                     UnhideCommand, RunAdvancedCommand, CancelCommand, CleanupRemoteModuleCommand })
        {
            if (command is RelayCommand relay) relay.RaiseCanExecuteChanged();
            if (command is AsyncRelayCommand asyncRelay) asyncRelay.RaiseCanExecuteChanged();
        }
    }

    public void SaveSettings() => _settingsService.Save(_settings);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SaveSettings();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _host.EventReceived -= OnInvocationEvent;
        _log.EntryWritten -= OnLogEntry;
    }
}
