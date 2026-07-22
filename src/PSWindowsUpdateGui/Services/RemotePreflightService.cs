using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;

namespace PSWindowsUpdateGui.Services;

internal sealed class RemotePreflightService
{
    private const string ProbeScript = @"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$systemDrive = Get-CimInstance -ClassName Win32_LogicalDisk -Filter ""DeviceID='C:'"" -ErrorAction Stop
$rebootPending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
                 (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
[pscustomobject]@{
    ComputerName = $env:COMPUTERNAME
    OsBuild = [Environment]::OSVersion.Version.Build
    Is64Bit = [Environment]::Is64BitOperatingSystem
    IsAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    ExecutionPolicy = (Get-ExecutionPolicy)
    ScheduledTasksAvailable = Test-Path (Join-Path $env:SystemRoot 'System32\schtasks.exe')
    ProgramData = $env:ProgramData
    UpdateService = (Get-Service -Name wuauserv -ErrorAction Stop).Status.ToString()
    RebootPending = $rebootPending
    Clock = [DateTimeOffset]::Now.ToString('O')
    FreeSpaceBytes = [int64]$systemDrive.FreeSpace
}";

    public RemotePreflightResult Run(string computerName, bool useSsl)
    {
        RemoteCliClient.ValidateComputerName(computerName);
        var result = new RemotePreflightResult(computerName, useSsl);

        try
        {
            var addresses = Dns.GetHostAddresses(computerName);
            result.Add("Name resolution", addresses.Length > 0, string.Join(", ", addresses.Select(address => address.ToString())));
        }
        catch (Exception exception)
        {
            result.Add("Name resolution", false, exception.Message);
            return result;
        }

        PSObject remote;
        try
        {
            using var powerShell = PowerShell.Create();
            powerShell.AddCommand("Invoke-Command")
                .AddParameter("ComputerName", computerName)
                .AddParameter("ScriptBlock", ScriptBlock.Create(ProbeScript))
                .AddParameter("ErrorAction", ActionPreference.Stop);
            if (useSsl)
            {
                powerShell.AddParameter("UseSSL", true);
            }

            remote = powerShell.Invoke().FirstOrDefault() ?? throw new InvalidOperationException("The remote probe returned no data.");
            if (powerShell.HadErrors)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, powerShell.Streams.Error.Select(error => error.ToString())));
            }

            result.Add(useSsl ? "WinRM HTTPS" : "WinRM Kerberos/Negotiate", true, "Connected with normal certificate and authentication validation.");
        }
        catch (Exception exception)
        {
            result.Add(useSsl ? "WinRM HTTPS" : "WinRM Kerberos/Negotiate", false, exception.Message);
            return result;
        }

        var build = Read<int>(remote, "OsBuild");
        var is64Bit = Read<bool>(remote, "Is64Bit");
        var isAdministrator = Read<bool>(remote, "IsAdministrator");
        var powerShellVersion = Read<string>(remote, "PowerShellVersion") ?? string.Empty;
        var tasks = Read<bool>(remote, "ScheduledTasksAvailable");
        var freeSpace = Read<long>(remote, "FreeSpaceBytes");
        var clock = Read<string>(remote, "Clock") ?? "Unknown";

        result.Add("Windows 11", build >= 22000, $"Build {build}");
        result.Add("x64 operating system", is64Bit, is64Bit ? "64-bit" : "Unsupported architecture");
        result.Add("Remote administrator token", isAdministrator, isAdministrator ? "Available" : "The remote session is not elevated.");
        result.Add("Windows PowerShell 5.1", powerShellVersion.StartsWith("5.1", StringComparison.Ordinal), powerShellVersion);
        result.Add("Task Scheduler", tasks, tasks ? "schtasks.exe is available" : "schtasks.exe was not found.");
        result.Add("Execution policy", true, Read<string>(remote, "ExecutionPolicy") ?? "Unknown");
        result.Add("Windows Update service", true, Read<string>(remote, "UpdateService") ?? "Unknown");
        result.Add("Reboot pending", true, Read<bool>(remote, "RebootPending").ToString());
        result.Add("Native WUA", true, "The staged executable will use the target's Windows Update Agent.");
        result.Add("Target-local clock", true, clock);
        result.Add("System drive free space", freeSpace >= 1_073_741_824L, FormatBytes(freeSpace));

        try
        {
            var programData = Read<string>(remote, "ProgramData") ?? @"C:\ProgramData";
            var driveRoot = Path.GetPathRoot(programData) ?? throw new InvalidDataException("ProgramData is not on a drive-letter path.");
            var relative = programData.Substring(driveRoot.Length).TrimStart('\\');
            var uncProgramData = Path.Combine($@"\\{computerName}\{char.ToUpperInvariant(driveRoot[0])}$", relative);
            var stageRoot = Path.Combine(uncProgramData, "PSWindowsUpdateGUI", "Remote");
            result.Add("SMB staging access", Directory.Exists(uncProgramData), stageRoot);
        }
        catch (Exception exception)
        {
            result.Add("SMB staging access", false, exception.Message);
        }

        return result;
    }

    private static T Read<T>(PSObject value, string property)
    {
        var raw = value.Properties[property]?.Value;
        if (raw == null)
        {
            return default!;
        }

        return (T)LanguagePrimitives.ConvertTo(raw, typeof(T));
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:N1} GiB";
}

internal sealed class RemotePreflightResult
{
    private readonly List<RemotePreflightCheck> _checks = new List<RemotePreflightCheck>();

    public RemotePreflightResult(string computerName, bool useSsl)
    {
        ComputerName = computerName;
        UseSsl = useSsl;
    }

    public string ComputerName { get; }

    public bool UseSsl { get; }

    public IReadOnlyList<RemotePreflightCheck> Checks => _checks;

    public bool Succeeded => _checks.Count >= 12 && _checks.Where(check => check.Required).All(check => check.Passed);

    public void Add(string name, bool passed, string detail, bool required = true) =>
        _checks.Add(new RemotePreflightCheck(name, passed, detail, required));

    public override string ToString() => string.Join(Environment.NewLine,
        _checks.Select(check => $"{(check.Passed ? "PASS" : check.Required ? "FAIL" : "WARN"),4}  {check.Name}: {check.Detail}"));
}

internal sealed class RemotePreflightCheck
{
    public RemotePreflightCheck(string name, bool passed, string detail, bool required)
    {
        Name = name;
        Passed = passed;
        Detail = detail;
        Required = required;
    }

    public string Name { get; }

    public bool Passed { get; }

    public string Detail { get; }

    public bool Required { get; }
}
