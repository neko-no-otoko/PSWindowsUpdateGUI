using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.Cli;

internal sealed class CliApplication
{
    public async Task<int> RunAsync(string[] rawArguments)
    {
        var arguments = CliArguments.Parse(rawArguments);
        ValidateOutput(arguments.Get("output", "text"));
        RejectSecretArguments(arguments);
        if (arguments.Positionals.Count == 0 || arguments.Has("help") || arguments.Positionals[0] == "help")
        {
            WriteHelp();
            return 0;
        }

        if (arguments.Has("computer"))
            return await new RemoteCliClient().ExecuteAsync(rawArguments, arguments.Get("computer"), arguments.Has("use-ssl")).ConfigureAwait(false);

        using var engine = new WuaWindowsUpdateEngine();
        var command = arguments.Positionals[0].ToLowerInvariant();
        switch (command)
        {
            case "scan":
            case "offline-scan":
                return await ScanAsync(engine, arguments, command == "offline-scan").ConfigureAwait(false);
            case "download":
            case "install":
            case "uninstall":
            case "hide":
            case "unhide":
                return await ModifyAsync(engine, arguments, command).ConfigureAwait(false);
            case "history":
                return await ReadAsync(() => engine.GetHistoryAsync(arguments.GetInt("limit", 100), CancellationToken.None), arguments).ConfigureAwait(false);
            case "status":
                return await ReadAsync(() => engine.GetStatusAsync(CancellationToken.None), arguments).ConfigureAwait(false);
            case "services":
                return await ServicesAsync(engine, arguments).ConfigureAwait(false);
            case "export-payload":
                return await ExportAsync(engine, arguments).ConfigureAwait(false);
            case "policy":
                return await PolicyAsync(arguments).ConfigureAwait(false);
            case "maintenance":
                return await MaintenanceAsync(arguments).ConfigureAwait(false);
            case "job":
                return await JobsAsync(rawArguments, arguments).ConfigureAwait(false);
            case "report":
                return await ReportsAsync(arguments).ConfigureAwait(false);
            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                WriteHelp();
                return 2;
        }
    }

    private static async Task<int> ScanAsync(IWindowsUpdateEngine engine, CliArguments arguments, bool offline)
    {
        var request = BuildScanRequest(arguments);
        if (offline)
        {
            request.Source = UpdateSourceKind.Offline;
            request.OfflineCabPath = arguments.Get("cab");
            if (arguments.Has("download-cab"))
            {
                var destination = arguments.Get("download-cab");
                var downloaded = await new OfflineCatalogService().DownloadAsync(destination, CancellationToken.None).ConfigureAwait(false);
                request.OfflineCabPath = downloaded.Path;
                if (!IsJson(arguments)) Console.Error.WriteLine($"Downloaded wsusscn2.cab: {downloaded.SizeBytes} bytes; SHA-256 {downloaded.Sha256}");
            }
        }

        return await RunEnvelopeAsync(
            () => engine.ScanAsync(request, CreateProgress(arguments), CancellationToken.None),
            arguments,
            data => $"{data.Count} update(s) found.{Environment.NewLine}" + string.Join(Environment.NewLine, data.Select(FormatUpdate))).ConfigureAwait(false);
    }

    private static async Task<int> ModifyAsync(IWindowsUpdateEngine engine, CliArguments arguments, string command)
    {
        if (command == "uninstall" && (arguments.Has("package") || (arguments.Has("kb") && !arguments.Has("update"))))
        {
            var plan = arguments.Has("plan");
            if (!plan && !Confirm(arguments, "Uninstall the explicitly identified Windows package without restarting automatically?")) return 2;
            var output = await new PackageMaintenanceService().UninstallAsync(arguments.Get("package"), arguments.Get("kb"), plan, CancellationToken.None).ConfigureAwait(false);
            WriteValue(arguments, output, string.Join(Environment.NewLine, output));
            return 0;
        }

        var action = (UpdateActionKind)Enum.Parse(typeof(UpdateActionKind), command, true);
        var request = new UpdateActionRequest
        {
            Action = action,
            Updates = arguments.GetAll("update").Select(UpdateKey.Parse).ToList(),
            Source = ParseSource(arguments.Get("source", "default")),
            ServiceId = arguments.Get("service-id"),
            AcceptEulas = arguments.Has("accept-eula"),
            Force = arguments.Has("force"),
            PlanOnly = arguments.Has("plan"),
            TimeoutSeconds = arguments.GetInt("timeout", 7200)
        };

        if (!request.PlanOnly && !Confirm(arguments, $"{action} {request.Updates.Count} explicitly identified update(s) on {Environment.MachineName}?"))
            return 2;

        return await RunEnvelopeAsync(
            () => engine.ExecuteAsync(request, CreateProgress(arguments), CancellationToken.None),
            arguments,
            data => $"{data.Action}: {data.Result}; reboot required: {data.RebootRequired}" + Environment.NewLine +
                    string.Join(Environment.NewLine, data.Updates.Select(item => $"{item.Identity}  {item.Result}  {item.Title}")),
            data => ClassifyResult(data, request.PlanOnly)).ConfigureAwait(false);
    }

    private static async Task<int> ServicesAsync(IWindowsUpdateEngine engine, CliArguments arguments)
    {
        var verb = arguments.Positionals.Count > 1 ? arguments.Positionals[1].ToLowerInvariant() : "list";
        if (verb == "list") return await ReadAsync(() => engine.GetServicesAsync(CancellationToken.None), arguments).ConfigureAwait(false);
        if (verb == "add-microsoft-update")
        {
            var plan = arguments.Has("plan");
            if (!plan && !Confirm(arguments, "Register Microsoft Update as an update service?")) return 2;
            return await ReadAsync(() => engine.AddMicrosoftUpdateServiceAsync(plan, CancellationToken.None), arguments).ConfigureAwait(false);
        }
        if (verb == "remove")
        {
            var id = arguments.Get("service-id");
            var plan = arguments.Has("plan");
            if (!plan && !Confirm(arguments, $"Remove update service {id}?")) return 2;
            await engine.RemoveServiceAsync(id, plan, CancellationToken.None).ConfigureAwait(false);
            WriteValue(arguments, new { serviceId = id, planned = plan }, plan ? "Service removal planned." : "Service removed.");
            return 0;
        }
        throw new FormatException("services supports list, add-microsoft-update, or remove.");
    }

    private static async Task<int> ExportAsync(IWindowsUpdateEngine engine, CliArguments arguments)
    {
        var updates = arguments.GetAll("update").Select(UpdateKey.Parse).ToList();
        var destination = arguments.Get("destination");
        if (updates.Count == 0) throw new FormatException("At least one --update identity is required.");
        if (string.IsNullOrWhiteSpace(destination)) throw new FormatException("--destination is required.");
        if (arguments.Has("plan"))
        {
            WriteValue(arguments, new { destination = Path.GetFullPath(destination), count = updates.Count, planned = true }, $"Would export cached payloads for {updates.Count} update(s) to {Path.GetFullPath(destination)}.");
            return 0;
        }
        if (!Confirm(arguments, $"Copy cached payloads for {updates.Count} update(s) to {destination}?")) return 2;
        await engine.ExportPayloadsAsync(updates, destination, CancellationToken.None).ConfigureAwait(false);
        WriteValue(arguments, new { destination, count = updates.Count }, $"Exported cached payloads to {destination}.");
        return 0;
    }

    private static async Task<int> PolicyAsync(CliArguments arguments)
    {
        var service = new WindowsUpdatePolicyService();
        var verb = arguments.Positionals.Count > 1 ? arguments.Positionals[1].ToLowerInvariant() : "get";
        if (verb == "get") { WriteValue(arguments, service.Read(), service.Render()); return 0; }
        if (verb == "set")
        {
            var changes = arguments.GetAll("value");
            var plan = arguments.Has("plan");
            var preview = service.Preview(changes);
            if (!plan && !Confirm(arguments, preview)) return 2;
            var backup = string.Empty;
            if (!plan)
            {
                var settings = new PortableSettingsService();
                backup = service.Backup(Path.Combine(settings.DataDirectory, "PolicyBackups"));
                service.Apply(changes);
            }
            WriteValue(arguments, service.Read(), plan ? preview : $"Policy values updated. Backup: {backup}");
            return 0;
        }
        if (verb == "restore")
        {
            var backup = arguments.Get("backup");
            var plan = arguments.Has("plan");
            if (!plan && !Confirm(arguments, $"Restore Windows Update policy from {backup}?")) return 2;
            if (!plan) service.Restore(backup);
            WriteValue(arguments, new { backup, planned = plan }, plan ? "Policy restore planned." : "Policy restored.");
            return 0;
        }
        throw new FormatException("policy supports get, set, or restore.");
    }

    private static async Task<int> MaintenanceAsync(CliArguments arguments)
    {
        await Task.Yield();
        var verb = arguments.Positionals.Count > 1 ? arguments.Positionals[1].ToLowerInvariant() : string.Empty;
        if (verb != "reset-components") throw new FormatException("maintenance supports reset-components.");
        var plan = arguments.Has("plan");
        if (!plan && !Confirm(arguments, "Stop update services and preserve-reset SoftwareDistribution and catroot2?")) return 2;
        var result = await new WindowsUpdateMaintenanceService().ResetAsync(plan, CancellationToken.None).ConfigureAwait(false);
        WriteValue(arguments, result, string.Join(Environment.NewLine, result));
        return 0;
    }

    private static Task<int> JobsAsync(string[] rawArguments, CliArguments arguments) =>
        new ScheduledJobService().ExecuteCliAsync(rawArguments, arguments);

    private static Task<int> ReportsAsync(CliArguments arguments) =>
        new ReportService().ExecuteCliAsync(arguments);

    private static ScanRequest BuildScanRequest(CliArguments arguments)
    {
        var request = new ScanRequest
        {
            Source = ParseSource(arguments.Get("source", "default")),
            Type = ParseType(arguments.Get("type", "all")),
            IncludeInstalled = arguments.Has("include-installed"),
            IncludeHidden = arguments.Has("include-hidden"),
            TitlePattern = arguments.Get("title"),
            Criteria = arguments.Get("criteria"),
            ServiceId = arguments.Get("service-id"),
            TimeoutSeconds = arguments.GetInt("timeout", 1800)
        };
        foreach (var kb in arguments.GetAll("kb").SelectMany(value => value.Split(',')))
            if (!string.IsNullOrWhiteSpace(kb)) request.KbArticleIds.Add(kb.Trim());
        return request;
    }

    private static UpdateSourceKind ParseSource(string value)
    {
        var normalized = value.Replace("-", string.Empty).Replace(" ", string.Empty);
        if (!Enum.TryParse(normalized, true, out UpdateSourceKind result) || !Enum.IsDefined(typeof(UpdateSourceKind), result))
            throw new FormatException("Source must be default, windows-update, microsoft-update, managed-server, service, or offline.");
        return result;
    }

    private static UpdateKind ParseType(string value)
    {
        if (!Enum.TryParse(value, true, out UpdateKind result) || !Enum.IsDefined(typeof(UpdateKind), result)) throw new FormatException("Type must be all, software, or driver.");
        return result;
    }

    private static IProgress<UpdateProgress>? CreateProgress(CliArguments arguments)
    {
        if (IsJson(arguments)) return null;
        return new Progress<UpdateProgress>(value => Console.Error.WriteLine($"{value.PercentComplete,3}% {value.Activity}"));
    }

    private static bool Confirm(CliArguments arguments, string summary)
    {
        if (arguments.Has("yes")) return true;
        if (Console.IsInputRedirected) throw new InvalidOperationException("A mutating noninteractive command requires --yes.");
        Console.Error.WriteLine(summary);
        Console.Error.Write("Type YES to continue: ");
        return string.Equals(Console.ReadLine(), "YES", StringComparison.Ordinal);
    }

    private static async Task<int> ReadAsync<T>(Func<Task<T>> action, CliArguments arguments)
    {
        return await RunEnvelopeAsync(action, arguments, value => RenderObject(value)).ConfigureAwait(false);
    }

    private static async Task<int> RunEnvelopeAsync<T>(
        Func<Task<T>> action,
        CliArguments arguments,
        Func<T, string> render,
        Func<T, OperationState>? classify = null)
    {
        var envelope = new OperationEnvelope<T>();
        try
        {
            envelope.Data = await action().ConfigureAwait(false);
            envelope.Status = classify?.Invoke(envelope.Data) ?? OperationState.Success;
        }
        catch (OperationCanceledException exception)
        {
            envelope.Status = OperationState.Cancelled;
            envelope.Errors.Add(ToError(exception));
        }
        catch (Exception exception)
        {
            envelope.Status = OperationState.Failed;
            envelope.Errors.Add(ToError(exception));
        }
        envelope.CompletedUtc = DateTime.UtcNow;

        if (IsJson(arguments)) Serialize(Console.Out, envelope);
        else if (envelope.Data != null) Console.WriteLine(render(envelope.Data));
        foreach (var error in envelope.Errors) Console.Error.WriteLine($"ERROR {error.Code}: {error.Message}");
        return ExitCode(envelope.Status);
    }

    private static OperationError ToError(Exception exception) => new OperationError
    {
        Code = $"0x{exception.HResult:X8}",
        HResult = exception.HResult,
        Message = PortableLogService.Redact(exception.Message)
    };

    private static int ExitCode(OperationState state)
    {
        if (state == OperationState.Success || state == OperationState.Planned) return 0;
        if (state == OperationState.RebootRequired) return 3010;
        if (state == OperationState.Partial) return 3;
        if (state == OperationState.Cancelled || state == OperationState.MonitoringStopped) return 4;
        return 1;
    }

    private static OperationState ClassifyResult(UpdateActionResult data, bool planOnly)
    {
        if (planOnly || string.Equals(data.Result, "Planned", StringComparison.OrdinalIgnoreCase)) return OperationState.Planned;
        if (data.Result.IndexOf("SucceededWithErrors", StringComparison.OrdinalIgnoreCase) >= 0) return OperationState.Partial;
        if (data.Result.IndexOf("Aborted", StringComparison.OrdinalIgnoreCase) >= 0) return OperationState.Cancelled;
        if (data.Result.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0 || data.HResult < 0) return OperationState.Failed;
        return data.RebootRequired ? OperationState.RebootRequired : OperationState.Success;
    }

    private static void ValidateOutput(string value)
    {
        if (!string.Equals(value, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "json", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "jsonl", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("--output must be text, json, or jsonl.");
    }

    private static void RejectSecretArguments(CliArguments arguments)
    {
        var forbidden = new[] { "password", "secret", "token", "authorization", "credential-password", "smtp-password" };
        var supplied = forbidden.FirstOrDefault(arguments.Has);
        if (supplied != null) throw new FormatException($"--{supplied} is forbidden. Secrets are never accepted in process arguments.");
    }

    private static bool IsJson(CliArguments arguments) =>
        string.Equals(arguments.Get("output"), "json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arguments.Get("output"), "jsonl", StringComparison.OrdinalIgnoreCase);

    private static void WriteValue<T>(CliArguments arguments, T value, string text)
    {
        if (IsJson(arguments)) Serialize(Console.Out, value);
        else Console.WriteLine(text);
    }

    private static void Serialize<T>(TextWriter writer, T value)
    {
        var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, value);
        writer.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string FormatUpdate(UpdateRecord update)
    {
        var kb = update.KbArticleIds.Count == 0 ? string.Empty : "KB" + string.Join(",KB", update.KbArticleIds);
        var driver = update.Type == "Driver" ? $"  {update.DriverProvider} {update.DriverVersion}" : string.Empty;
        return $"{update.Identity}  {update.Type,-8} {FormatBytes(update.MaximumDownloadBytes),10}  {kb,-18} {update.Title}{driver}";
    }

    private static string FormatBytes(decimal bytes)
    {
        if (bytes >= 1024m * 1024m * 1024m) return $"{bytes / 1024m / 1024m / 1024m:N1} GiB";
        if (bytes >= 1024m * 1024m) return $"{bytes / 1024m / 1024m:N1} MiB";
        if (bytes >= 1024m) return $"{bytes / 1024m:N1} KiB";
        return $"{bytes:N0} B";
    }

    private static string RenderObject(object? value)
    {
        if (value == null) return string.Empty;
        if (value is System.Collections.IEnumerable values && !(value is string))
            return string.Join(Environment.NewLine, values.Cast<object>().Select(RenderObject));
        var properties = value.GetType().GetProperties().Where(property => property.CanRead);
        return string.Join("  ", properties.Select(property => $"{property.Name}={property.GetValue(value)}"));
    }

    private static void WriteHelp()
    {
        Console.WriteLine("PSWindowsUpdateGUI 2 - independent Windows Update Agent GUI and CLI");
        Console.WriteLine();
        Console.WriteLine("Usage: PSWindowsUpdateGUI.exe <command> [options]");
        Console.WriteLine("Commands: scan, download, install, uninstall, hide, unhide, history, status,");
        Console.WriteLine("          services, offline-scan, export-payload, policy, maintenance, job, report");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --computer <dns-name>  --use-ssl  --source <source>  --type <all|software|driver>");
        Console.WriteLine("  --update <guid>:<revision> (repeatable)  --criteria <WUA criteria>");
        Console.WriteLine("  --plan  --yes  --output <text|json|jsonl>  --timeout <seconds>");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  PSWindowsUpdateGUI.exe scan --type driver --output json");
        Console.WriteLine("  PSWindowsUpdateGUI.exe install --update <guid>:<revision> --accept-eula --plan");
        Console.WriteLine("  PSWindowsUpdateGUI.exe offline-scan --cab C:\\wsusscn2.cab --output json");
        Console.WriteLine("  PSWindowsUpdateGUI.exe offline-scan --download-cab C:\\wsusscn2.cab --output json");
    }
}
