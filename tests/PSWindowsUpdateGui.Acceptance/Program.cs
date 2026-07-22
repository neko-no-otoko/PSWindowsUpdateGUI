using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PSWindowsUpdateGui.Cli;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.Acceptance;

[DataContract]
internal sealed class AcceptanceReport
{
    [DataMember(Name = "startedUtc", Order = 1)] public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    [DataMember(Name = "completedUtc", Order = 2)] public DateTime CompletedUtc { get; set; }
    [DataMember(Name = "computerName", Order = 3)] public string ComputerName { get; set; } = Environment.MachineName;
    [DataMember(Name = "operations", Order = 4)] public IList<AcceptanceOperation> Operations { get; set; } = new List<AcceptanceOperation>();
    [DataMember(Name = "drivers", Order = 5)] public IList<UpdateRecord> Drivers { get; set; } = new List<UpdateRecord>();
}

[DataContract]
internal sealed class AcceptanceOperation
{
    [DataMember(Name = "name", Order = 1)] public string Name { get; set; } = string.Empty;
    [DataMember(Name = "succeeded", Order = 2)] public bool Succeeded { get; set; }
    [DataMember(Name = "detail", Order = 3)] public string Detail { get; set; } = string.Empty;
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var report = new AcceptanceReport();
        using var engine = new WuaWindowsUpdateEngine();
        try
        {
            var status = await engine.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
            report.Operations.Add(new AcceptanceOperation { Name = "status", Succeeded = true, Detail = $"WUA {status.AgentVersion}; service={status.UpdateServiceStatus}; reboot={status.RebootRequired}; busy={status.InstallerBusy}" });

            var updates = await engine.ScanAsync(new ScanRequest(), null, CancellationToken.None).ConfigureAwait(false);
            report.Operations.Add(new AcceptanceOperation { Name = "default-scan", Succeeded = true, Detail = $"{updates.Count} applicable update(s)" });

            var drivers = await engine.ScanAsync(new ScanRequest { Type = UpdateKind.Driver, IncludeHidden = true }, null, CancellationToken.None).ConfigureAwait(false);
            report.Drivers = drivers.ToList();
            report.Operations.Add(new AcceptanceOperation { Name = "driver-scan", Succeeded = true, Detail = $"{drivers.Count} applicable driver update(s)" });

            if (drivers.Count > 0)
            {
                var planned = await engine.ExecuteAsync(new UpdateActionRequest
                {
                    Action = UpdateActionKind.Install,
                    Updates = new[] { drivers[0].Identity },
                    AcceptEulas = true,
                    PlanOnly = true
                }, null, CancellationToken.None).ConfigureAwait(false);
                report.Operations.Add(new AcceptanceOperation
                {
                    Name = "specific-driver-plan",
                    Succeeded = planned.Result == "Planned" && planned.Updates.Count == 1,
                    Detail = $"{drivers[0].Identity}; {drivers[0].DriverProvider}; {drivers[0].DriverVersion}; {planned.Result}"
                });

                var previousOutput = Console.Out;
                using var cliOutput = new StringWriter();
                int cliExitCode;
                try
                {
                    Console.SetOut(cliOutput);
                    cliExitCode = await new CliApplication().RunAsync(new[]
                    {
                        "install", "--update", drivers[0].Identity.ToString(), "--plan", "--output", "json"
                    }).ConfigureAwait(false);
                }
                finally { Console.SetOut(previousOutput); }
                using var json = new MemoryStream(Encoding.UTF8.GetBytes(cliOutput.ToString()));
                var cliEnvelope = (OperationEnvelope<UpdateActionResult>?)new DataContractJsonSerializer(typeof(OperationEnvelope<UpdateActionResult>)).ReadObject(json);
                report.Operations.Add(new AcceptanceOperation
                {
                    Name = "specific-driver-cli-json-plan",
                    Succeeded = cliExitCode == 0 && cliEnvelope?.Status == OperationState.Planned && cliEnvelope.Data?.Updates.Count == 1,
                    Detail = $"exit={cliExitCode}; state={cliEnvelope?.Status}; identity={drivers[0].Identity}"
                });
            }

            var criteria = await engine.ScanAsync(new ScanRequest { Criteria = "IsInstalled=0 and Type='Software'" }, null, CancellationToken.None).ConfigureAwait(false);
            report.Operations.Add(new AcceptanceOperation { Name = "validated-criteria-scan", Succeeded = true, Detail = $"{criteria.Count} applicable software update(s)" });

            var services = await engine.GetServicesAsync(CancellationToken.None).ConfigureAwait(false);
            report.Operations.Add(new AcceptanceOperation { Name = "service-list", Succeeded = true, Detail = $"{services.Count} registered update service(s)" });

            var history = await engine.GetHistoryAsync(25, CancellationToken.None).ConfigureAwait(false);
            report.Operations.Add(new AcceptanceOperation { Name = "history", Succeeded = true, Detail = $"{history.Count} recent record(s)" });

            if (args.Contains("--install-driver", StringComparer.OrdinalIgnoreCase))
            {
                var marker = Array.FindIndex(args, value => string.Equals(value, "--update", StringComparison.OrdinalIgnoreCase));
                if (!args.Contains("--confirm-machine-mutation", StringComparer.OrdinalIgnoreCase) || marker < 0 || marker + 1 >= args.Length)
                    throw new InvalidOperationException("Driver installation requires --update <guid>:<revision> and --confirm-machine-mutation on a snapshot-backed test machine.");
                var key = UpdateKey.Parse(args[marker + 1]);
                if (!drivers.Any(driver => driver.Identity.Equals(key))) throw new InvalidOperationException("The selected driver identity is not in the current applicable driver scan.");
                var result = await engine.ExecuteAsync(new UpdateActionRequest { Action = UpdateActionKind.Install, Updates = new[] { key }, AcceptEulas = true }, null, CancellationToken.None).ConfigureAwait(false);
                report.Operations.Add(new AcceptanceOperation { Name = "explicit-driver-install", Succeeded = result.HResult >= 0, Detail = $"{result.Result}; reboot={result.RebootRequired}" });
            }
        }
        catch (Exception exception)
        {
            report.Operations.Add(new AcceptanceOperation { Name = "fatal", Succeeded = false, Detail = exception.ToString() });
        }
        report.CompletedUtc = DateTime.UtcNow;
        var output = args.SkipWhile(value => !string.Equals(value, "--output", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()
                     ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "acceptance", $"native-wua-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using (var stream = File.Create(output)) new DataContractJsonSerializer(typeof(AcceptanceReport)).WriteObject(stream, report);
        Console.WriteLine(output);
        return report.Operations.All(operation => operation.Succeeded) ? 0 : 1;
    }
}
