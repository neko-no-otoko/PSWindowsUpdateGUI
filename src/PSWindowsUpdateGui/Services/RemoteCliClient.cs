using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace PSWindowsUpdateGui.Services;

internal sealed class RemoteCliClient
{
    private const string MarkerName = "PSWindowsUpdateGUI.owner";
    private static readonly Regex DnsName = new Regex(@"^(?=.{1,253}$)(?!-)(?:[A-Za-z0-9-]{1,63}\.)*[A-Za-z0-9](?:[A-Za-z0-9-]{0,62})$", RegexOptions.Compiled);

    private const string PreflightScript = @"
param($useSsl)
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
[pscustomobject]@{
  ComputerName = $env:COMPUTERNAME
  ProgramData = $env:ProgramData
  OsBuild = [Environment]::OSVersion.Version.Build
  Is64Bit = [Environment]::Is64BitOperatingSystem
  IsAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
  PowerShellVersion = $PSVersionTable.PSVersion.ToString()
  ScheduledTasksAvailable = Test-Path (Join-Path $env:SystemRoot 'System32\schtasks.exe')
  FreeSpace = (Get-CimInstance Win32_LogicalDisk -Filter ""DeviceID='$($env:SystemDrive)'"").FreeSpace
}";

    private const string ExecuteScript = @"
param($exePath, $arguments, $expectedHash)
$actual = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256 -ErrorAction Stop).Hash
if ($actual -ne $expectedHash) { throw 'The staged PSWindowsUpdateGUI executable failed SHA-256 verification.' }
$output = & $exePath @arguments 2>&1 | ForEach-Object { $_.ToString() }
[pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join [Environment]::NewLine) }
";

    public async Task<int> ExecuteAsync(string[] rawArguments, string computerName, bool useSsl)
    {
        ValidateComputerName(computerName);
        return await Task.Run(() => Execute(rawArguments, computerName, useSsl)).ConfigureAwait(false);
    }

    public static void ValidateComputerName(string computerName)
    {
        if (string.IsNullOrWhiteSpace(computerName) || !DnsName.IsMatch(computerName) || IPAddress.TryParse(computerName, out _))
            throw new FormatException("Remote computer must be a DNS hostname, not a path or IP address.");
    }

    private static int Execute(string[] rawArguments, string computerName, bool useSsl)
    {
        var preflight = Invoke(computerName, useSsl, PreflightScript, useSsl);
        var remote = preflight.FirstOrDefault() ?? throw new InvalidOperationException("Remote preflight returned no data.");
        if (!Read<bool>(remote, "Is64Bit") || Read<int>(remote, "OsBuild") < 22000)
            throw new PlatformNotSupportedException("The remote target must be Windows 11 x64.");
        if (!Read<bool>(remote, "IsAdministrator")) throw new UnauthorizedAccessException("The remote WinRM session does not have an administrator token.");
        var powerShellVersion = Read<string>(remote, "PowerShellVersion") ?? string.Empty;
        if (!powerShellVersion.StartsWith("5.1", StringComparison.Ordinal)) throw new PlatformNotSupportedException("The remote target must provide Windows PowerShell 5.1 for the fixed WinRM transport.");
        if (!Read<bool>(remote, "ScheduledTasksAvailable") && rawArguments.Length > 0 && string.Equals(rawArguments[0], "job", StringComparison.OrdinalIgnoreCase))
            throw new PlatformNotSupportedException("Task Scheduler is unavailable on the remote target.");
        if (Read<long>(remote, "FreeSpace") < 1_073_741_824L) throw new IOException("The remote system drive has less than 1 GiB free.");

        var localExe = Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Could not locate the current executable.");
        var hash = Hash(localExe);
        var programData = Read<string>(remote, "ProgramData") ?? throw new InvalidOperationException("Remote ProgramData was not reported.");
        var driveRoot = Path.GetPathRoot(programData);
        if (driveRoot == null || driveRoot.Length < 2 || driveRoot[1] != ':') throw new InvalidDataException("Remote ProgramData is not on a drive-letter path.");
        var relativeProgramData = programData.Substring(driveRoot.Length).TrimStart('\\');
        var remoteDirectory = Path.Combine(programData, "PSWindowsUpdateGUI", "Remote", hash.Substring(0, 16));
        var uncAppRoot = $@"\\{computerName}\{char.ToUpperInvariant(driveRoot[0])}$\{relativeProgramData}\PSWindowsUpdateGUI";
        var uncRemoteRoot = Path.Combine(uncAppRoot, "Remote");
        var uncJobRoot = Path.Combine(uncAppRoot, "Jobs");
        var uncDirectory = Path.Combine(uncRemoteRoot, hash.Substring(0, 16));
        var uncExe = Path.Combine(uncDirectory, "PSWindowsUpdateGUI.exe");
        var marker = Path.Combine(uncDirectory, MarkerName);

        ReconcileStaleOwned(uncRemoteRoot, uncJobRoot, hash);
        Directory.CreateDirectory(uncDirectory);
        RestrictDirectory(uncDirectory);
        if (File.Exists(marker) && !string.Equals(File.ReadAllText(marker).Trim(), hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The remote staging directory has a foreign ownership marker.");
        if (File.Exists(uncExe) && !File.Exists(marker))
            throw new InvalidDataException("The remote staging directory contains an unowned executable and will not be claimed or overwritten.");
        if (File.Exists(uncExe) && !string.Equals(Hash(uncExe), hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The remote staging directory contains a different executable and will not be overwritten.");
        if (!File.Exists(uncExe)) File.Copy(localExe, uncExe, false);
        if (!File.Exists(marker)) File.WriteAllText(marker, hash, new UTF8Encoding(false));

        var forwarded = RemoveTransportOptions(rawArguments).ToArray();
        if (!forwarded.Any(value => string.Equals(value, "--output", StringComparison.OrdinalIgnoreCase)))
        {
            forwarded = forwarded.Concat(new[] { "--output", "json" }).ToArray();
        }
        var result = Invoke(computerName, useSsl, ExecuteScript, remoteDirectory + "\\PSWindowsUpdateGUI.exe", forwarded, hash).FirstOrDefault()
                     ?? throw new InvalidOperationException("The remote CLI returned no result.");
        var output = Read<string>(result, "Output") ?? string.Empty;
        if (output.Length > 0) Console.WriteLine(output);
        var exitCode = Read<int>(result, "ExitCode");

        var createsPersistentJob = forwarded.Length > 1 && string.Equals(forwarded[0], "job", StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(forwarded[1], "create", StringComparison.OrdinalIgnoreCase);
        if (!createsPersistentJob) TryCleanupOwned(uncDirectory, marker, uncExe, hash, uncJobRoot);
        return exitCode;
    }

    private static IEnumerable<string> RemoveTransportOptions(IList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], "--computer", StringComparison.OrdinalIgnoreCase)) { index++; continue; }
            if (string.Equals(arguments[index], "--use-ssl", StringComparison.OrdinalIgnoreCase)) continue;
            yield return arguments[index];
        }
    }

    private static IReadOnlyList<PSObject> Invoke(string computerName, bool useSsl, string script, params object[] arguments)
    {
        using var powerShell = PowerShell.Create();
        powerShell.AddCommand("Invoke-Command")
            .AddParameter("ComputerName", computerName)
            .AddParameter("ScriptBlock", ScriptBlock.Create(script))
            .AddParameter("ArgumentList", arguments)
            .AddParameter("ErrorAction", ActionPreference.Stop);
        if (useSsl) powerShell.AddParameter("UseSSL", true);
        var output = powerShell.Invoke();
        if (powerShell.HadErrors) throw new InvalidOperationException(string.Join(Environment.NewLine, powerShell.Streams.Error.Select(error => error.Exception.Message)));
        return output;
    }

    private static T Read<T>(PSObject value, string property)
    {
        var raw = value.Properties[property]?.Value;
        return raw == null ? default! : (T)LanguagePrimitives.ConvertTo(raw, typeof(T));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static void RestrictDirectory(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        Directory.SetAccessControl(path, security);
    }

    private static void ReconcileStaleOwned(string remoteRoot, string jobRoot, string currentHash)
    {
        try
        {
            if (!Directory.Exists(remoteRoot)) return;
            foreach (var directory in Directory.GetDirectories(remoteRoot))
            {
                var marker = Path.Combine(directory, MarkerName);
                var executable = Path.Combine(directory, "PSWindowsUpdateGUI.exe");
                if (!File.Exists(marker)) continue;
                var hash = File.ReadAllText(marker).Trim();
                if (string.Equals(hash, currentHash, StringComparison.OrdinalIgnoreCase)) continue;
                TryCleanupOwned(directory, marker, executable, hash, jobRoot);
            }
        }
        catch { }
    }

    private static bool HasActiveJobReference(string jobRoot, string hash)
    {
        try
        {
            if (!Directory.Exists(jobRoot)) return false;
            foreach (var path in Directory.GetFiles(jobRoot, "*.json"))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var job = (ScheduledUpdateManifest?)new DataContractJsonSerializer(typeof(ScheduledUpdateManifest)).ReadObject(stream);
                    if (job != null && string.Equals(job.ExecutableHash, hash, StringComparison.OrdinalIgnoreCase) &&
                        (job.State == "Scheduled" || job.State == "Running" || job.State == "RebootRequired")) return true;
                }
                catch { return true; }
            }
            return false;
        }
        catch { return true; }
    }

    private static void TryCleanupOwned(string directory, string marker, string executable, string hash, string jobRoot)
    {
        try
        {
            if (!Regex.IsMatch(hash, "^[A-Fa-f0-9]{64}$")) return;
            if (!File.Exists(marker) || !string.Equals(File.ReadAllText(marker).Trim(), hash, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(executable) && !string.Equals(Hash(executable), hash, StringComparison.OrdinalIgnoreCase)) return;
            if (HasActiveJobReference(jobRoot, hash)) return;
            if (File.Exists(executable)) File.Delete(executable);
            File.Delete(marker);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch { }
    }
}
