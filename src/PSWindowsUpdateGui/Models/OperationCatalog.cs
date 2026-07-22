using System.Collections.Generic;

namespace PSWindowsUpdateGui.Models;

internal sealed class OperationDefinition
{
    public OperationDefinition(string name, string category, bool mutating, string description)
    {
        Name = name; Category = category; Mutating = mutating; Description = description;
    }
    public string Name { get; }
    public string Category { get; }
    public bool Mutating { get; }
    public string Description { get; }
}

internal sealed class LegacyCapabilityMapping
{
    public LegacyCapabilityMapping(string legacyCommand, string replacement)
    {
        LegacyCommand = legacyCommand; Replacement = replacement;
    }
    public string LegacyCommand { get; }
    public string Replacement { get; }
}

internal static class OperationCatalog
{
    public static readonly IReadOnlyList<OperationDefinition> Operations = new[]
    {
        new OperationDefinition("scan", "Updates", false, "Search software or driver updates using typed WUA criteria."),
        new OperationDefinition("download", "Updates", true, "Download explicitly identified updates."),
        new OperationDefinition("install", "Updates", true, "Download and install explicitly identified updates without automatic reboot."),
        new OperationDefinition("uninstall", "Updates", true, "Uninstall an explicitly identified WUA-uninstallable update."),
        new OperationDefinition("hide", "Updates", true, "Hide explicitly identified updates."),
        new OperationDefinition("unhide", "Updates", true, "Unhide explicitly identified updates."),
        new OperationDefinition("history", "Status", false, "Read Windows Update Agent history."),
        new OperationDefinition("status", "Status", false, "Read agent, service, installer, clock, and reboot status."),
        new OperationDefinition("services", "Services", true, "List, register Microsoft Update, or remove an update service."),
        new OperationDefinition("offline-scan", "Offline", false, "Assess security updates using Microsoft-signed wsusscn2.cab metadata."),
        new OperationDefinition("export-payload", "Offline", true, "Copy already downloaded WUA cache contents to a selected directory."),
        new OperationDefinition("policy", "Administration", true, "Inspect, validate, back up, modify, or restore allowlisted Windows Update policy values."),
        new OperationDefinition("maintenance", "Administration", true, "Preserve-reset Windows Update data stores and restart their services."),
        new OperationDefinition("job", "Automation", true, "Create, run, inspect, cancel, or clean schema-validated scheduled update jobs."),
        new OperationDefinition("report", "Reporting", true, "Configure and send SMTP reports without storing passwords in portable state.")
    };

    public static readonly IReadOnlyList<LegacyCapabilityMapping> LegacyMappings = new[]
    {
        new LegacyCapabilityMapping("Add-WUServiceManager", "services add-microsoft-update"),
        new LegacyCapabilityMapping("Enable-WURemoting", "secure remote preflight and documentation; never changes WinRM automatically"),
        new LegacyCapabilityMapping("Get-WindowsUpdate", "scan/download/install/hide/unhide"),
        new LegacyCapabilityMapping("Get-WUApiVersion", "status"),
        new LegacyCapabilityMapping("Get-WUHistory", "history"),
        new LegacyCapabilityMapping("Get-WUInstallerStatus", "status"),
        new LegacyCapabilityMapping("Get-WUJob", "job list"),
        new LegacyCapabilityMapping("Get-WULastResults", "history/status and operation journal"),
        new LegacyCapabilityMapping("Get-WUOfflineMSU", "download plus export-payload"),
        new LegacyCapabilityMapping("Get-WURebootStatus", "status"),
        new LegacyCapabilityMapping("Get-WUServiceManager", "services list"),
        new LegacyCapabilityMapping("Get-WUSettings", "policy get"),
        new LegacyCapabilityMapping("Invoke-WUJob", "job create with a fixed manifest; arbitrary scripts intentionally removed"),
        new LegacyCapabilityMapping("Remove-WindowsUpdate", "uninstall with WUA capability checks"),
        new LegacyCapabilityMapping("Remove-WUServiceManager", "services remove"),
        new LegacyCapabilityMapping("Reset-WUComponents", "maintenance reset-components"),
        new LegacyCapabilityMapping("Set-PSWUSettings", "report configure/test/send"),
        new LegacyCapabilityMapping("Set-WUSettings", "policy set/restore"),
        new LegacyCapabilityMapping("Update-WUModule", "application release/checksum update workflow; no runtime module exists")
    };
}
