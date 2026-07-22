using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.Acceptance;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var outputArgument = args.FirstOrDefault(argument => argument.StartsWith("--output=", StringComparison.OrdinalIgnoreCase));
        var outputPath = outputArgument?.Substring("--output=".Length) ??
                         Path.Combine(Environment.CurrentDirectory, "local-acceptance.json");
        var installFirstSafeDriver = args.Any(argument =>
            string.Equals(argument, "--install-first-safe-driver", StringComparison.OrdinalIgnoreCase));

        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        if (!elevated)
        {
            throw new InvalidOperationException("The local acceptance runner must be elevated.");
        }

        var report = new AcceptanceReport
        {
            StartedUtc = DateTimeOffset.UtcNow,
            Identity = identity.Name,
            Elevated = true,
            WindowsBuild = App.GetWindowsBuildNumber(),
            RebootPendingBefore = IsRebootPending()
        };

        var reportDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(reportDirectory);
        var log = new PortableLogService(reportDirectory, new PortableSettings());
        using var module = ModuleRuntime.Create();
        using var host = new PowerShellHost(module, log);
        await host.InitializeAsync().ConfigureAwait(false);
        var catalog = await host.LoadCatalogAsync().ConfigureAwait(false);
        report.ModuleVersion = module.Manifest.PackageVersion;
        report.CatalogCommandCount = catalog.Count;

        foreach (var command in new[]
                 {
                     "Get-WUApiVersion", "Get-WUInstallerStatus", "Get-WULastResults",
                     "Get-WURebootStatus", "Get-WUServiceManager", "Get-WUSettings", "Get-WUJob"
                 })
        {
            var parameters = new Dictionary<string, object?>();
            if (string.Equals(command, "Get-WURebootStatus", StringComparison.OrdinalIgnoreCase))
            {
                parameters["Silent"] = new SwitchParameter(true);
            }

            report.Operations.Add(await InvokeAsync(host, command, parameters).ConfigureAwait(false));
        }

        var driverParameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["WindowsUpdate"] = new SwitchParameter(true),
            ["UpdateType"] = "Driver"
        };
        var driverScan = await host.InvokeAsync("Get-WindowsUpdate", driverParameters, CancellationToken.None).ConfigureAwait(false);
        report.Operations.Add(ToOperation("Get-WindowsUpdate -WindowsUpdate -UpdateType Driver", driverScan));
        report.DriverCandidates.AddRange(driverScan.Output.Select(UpdateRow.From)
            .Where(update => !string.IsNullOrWhiteSpace(update.Title))
            .Select(update => new DriverCandidate
            {
                Title = update.Title,
                UpdateId = update.UpdateId,
                KB = update.KB,
                Size = update.Size,
                Status = update.Status
            }));

        var filteredParameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["WindowsUpdate"] = new SwitchParameter(true),
            ["UpdateType"] = "Driver",
            ["Title"] = "Intel|Lenovo|NVIDIA|Realtek|AMD"
        };
        report.Operations.Add(await InvokeAsync(host,
            "Get-WindowsUpdate",
            filteredParameters,
            "Get-WindowsUpdate typed regex-filtered driver scan").ConfigureAwait(false));

        if (installFirstSafeDriver)
        {
            var candidate = report.DriverCandidates.FirstOrDefault(driver =>
                !string.IsNullOrWhiteSpace(driver.UpdateId) &&
                driver.Title.IndexOf("firmware", StringComparison.OrdinalIgnoreCase) < 0 &&
                driver.Title.IndexOf("bios", StringComparison.OrdinalIgnoreCase) < 0);
            if (report.RebootPendingBefore)
            {
                report.InstallAttempt = "Skipped: Windows already has a pending reboot.";
            }
            else if (candidate == null)
            {
                report.InstallAttempt = "Skipped: no non-firmware driver with an UpdateID was offered.";
            }
            else
            {
                var installParameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["WindowsUpdate"] = new SwitchParameter(true),
                    ["UpdateType"] = "Driver",
                    ["UpdateID"] = new[] { candidate.UpdateId },
                    ["Install"] = new SwitchParameter(true),
                    ["AcceptAll"] = new SwitchParameter(true),
                    ["IgnoreReboot"] = new SwitchParameter(true),
                    ["Confirm"] = false
                };
                report.Operations.Add(await InvokeAsync(host,
                    "Get-WindowsUpdate",
                    installParameters,
                    "Install selected driver: " + candidate.Title).ConfigureAwait(false));
                report.InstallAttempt = "Attempted: " + candidate.Title;
            }
        }

        report.RebootPendingAfter = IsRebootPending();
        report.CompletedUtc = DateTimeOffset.UtcNow;
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        File.WriteAllText(outputPath, serializer.Serialize(report));
        Console.WriteLine(outputPath);
        return report.Operations.All(operation => operation.Succeeded) ? 0 : 2;
    }

    private static async Task<AcceptanceOperation> InvokeAsync(
        PowerShellHost host,
        string command,
        IReadOnlyDictionary<string, object?> parameters,
        string? label = null)
    {
        var result = await host.InvokeAsync(command, parameters, CancellationToken.None).ConfigureAwait(false);
        return ToOperation(label ?? command, result);
    }

    private static AcceptanceOperation ToOperation(string name, InvocationResult result) => new AcceptanceOperation
    {
        Name = name,
        Succeeded = result.Succeeded,
        Output = result.Output.Select(value => value.ToString()).ToList(),
        OutputProperties = result.Output.Select(value => value.Properties.ToDictionary(
            property => property.Name,
            property => ReadProperty(property),
            StringComparer.OrdinalIgnoreCase)).ToList(),
        Errors = result.Errors.ToList()
    };

    private static string ReadProperty(PSPropertyInfo property)
    {
        try
        {
            return property.Value?.ToString() ?? string.Empty;
        }
        catch (Exception exception)
        {
            return "<error: " + exception.Message + ">";
        }
    }

    private static bool IsRebootPending()
    {
        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") != null ||
               localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") != null;
    }
}

internal sealed class AcceptanceReport
{
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public string Identity { get; set; } = string.Empty;
    public bool Elevated { get; set; }
    public int WindowsBuild { get; set; }
    public string ModuleVersion { get; set; } = string.Empty;
    public int CatalogCommandCount { get; set; }
    public bool RebootPendingBefore { get; set; }
    public bool RebootPendingAfter { get; set; }
    public string InstallAttempt { get; set; } = "Not requested";
    public List<AcceptanceOperation> Operations { get; } = new List<AcceptanceOperation>();
    public List<DriverCandidate> DriverCandidates { get; } = new List<DriverCandidate>();
}

internal sealed class AcceptanceOperation
{
    public string Name { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public List<string> Output { get; set; } = new List<string>();
    public List<Dictionary<string, string>> OutputProperties { get; set; } = new List<Dictionary<string, string>>();
    public List<string> Errors { get; set; } = new List<string>();
}

internal sealed class DriverCandidate
{
    public string Title { get; set; } = string.Empty;
    public string UpdateId { get; set; } = string.Empty;
    public string KB { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
