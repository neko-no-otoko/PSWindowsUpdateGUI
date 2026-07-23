using System;
using System.Globalization;
using PSWindowsUpdateGui.Infrastructure;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.ViewModels;

internal sealed class HistoryItemViewModel : ObservableObject
{
    private bool _canUninstall;
    private string _removalStatus;
    private string _verifiedSource = string.Empty;

    public HistoryItemViewModel(HistoryRecord record)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        _removalStatus = CanCheckRemoval
            ? "Select Check removal to ask Windows whether this exact installed revision can be removed."
            : "Only completed installation entries can be checked for removal.";
    }

    public HistoryRecord Record { get; }
    public DateTime DateUtc => Record.DateUtc;
    public string DateDisplay => DateUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string Title => Record.Title;
    public string Client => Record.Client;
    public UpdateKey Identity => Record.Identity;
    public string IdentityDisplay => Identity.ToString();
    public string HResultDisplay => $"0x{unchecked((uint)Record.HResult):X8}";
    public string OperationDisplay => DisplayOperation(Record.Operation);
    public string ResultDisplay => DisplayResult(Record.Result);

    public bool CanCheckRemoval => IsCompletedInstallation(Record);
    public bool CanUninstall { get => _canUninstall; private set => SetProperty(ref _canUninstall, value); }
    public string RemovalStatus { get => _removalStatus; private set => SetProperty(ref _removalStatus, value); }
    public string VerifiedSource { get => _verifiedSource; private set => SetProperty(ref _verifiedSource, value); }

    public void MarkRemovalVerified(string source)
    {
        VerifiedSource = source;
        CanUninstall = true;
        RemovalStatus = "Windows reports this exact installed revision as uninstallable. A driver may roll back only when Windows has a previous package available.";
    }

    public void MarkRemovalUnavailable(string message)
    {
        VerifiedSource = string.Empty;
        CanUninstall = false;
        RemovalStatus = string.IsNullOrWhiteSpace(message)
            ? "Windows did not report this exact revision as removable."
            : message;
    }

    public void MarkRemovalCompleted(bool rebootRequired)
    {
        VerifiedSource = string.Empty;
        CanUninstall = false;
        RemovalStatus = rebootRequired
            ? "Removal completed and Windows requires a restart. The audit-history entry remains."
            : "Removal completed. The audit-history entry remains.";
    }

    public void ResetRemovalVerification(string reason)
    {
        VerifiedSource = string.Empty;
        CanUninstall = false;
        RemovalStatus = CanCheckRemoval
            ? reason
            : "Only completed installation entries can be checked for removal.";
    }

    internal static bool IsCompletedInstallation(HistoryRecord record)
    {
        var installed = string.Equals(record.Operation, "uoInstallation", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Operation, "Installation", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Operation, "Install", StringComparison.OrdinalIgnoreCase);
        var succeeded = string.Equals(record.Result, "orcSucceeded", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Result, "orcSucceededWithErrors", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Result, "Succeeded", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Result, "SucceededWithErrors", StringComparison.OrdinalIgnoreCase);
        return installed && succeeded && Guid.TryParse(record.Identity.UpdateId, out _) && record.Identity.Revision >= 0;
    }

    private static string DisplayOperation(string value)
    {
        if (string.Equals(value, "uoInstallation", StringComparison.OrdinalIgnoreCase)) return "Installed";
        if (string.Equals(value, "uoUninstallation", StringComparison.OrdinalIgnoreCase)) return "Uninstalled";
        if (string.Equals(value, "uoOther", StringComparison.OrdinalIgnoreCase)) return "Other";
        return value;
    }

    private static string DisplayResult(string value)
    {
        if (string.Equals(value, "orcNotStarted", StringComparison.OrdinalIgnoreCase)) return "Not started";
        if (string.Equals(value, "orcInProgress", StringComparison.OrdinalIgnoreCase)) return "In progress";
        if (string.Equals(value, "orcSucceeded", StringComparison.OrdinalIgnoreCase)) return "Succeeded";
        if (string.Equals(value, "orcSucceededWithErrors", StringComparison.OrdinalIgnoreCase)) return "Succeeded with errors";
        if (string.Equals(value, "orcFailed", StringComparison.OrdinalIgnoreCase)) return "Failed";
        if (string.Equals(value, "orcAborted", StringComparison.OrdinalIgnoreCase)) return "Aborted";
        return value;
    }
}
