using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Services;

internal static class RemoteGuiBridge
{
    public static async Task<IReadOnlyList<UpdateRecord>> ScanAsync(ScanRequest request, string computerName, bool useSsl, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "scan", "--computer", computerName, "--source", FormatSource(request.Source), "--type", request.Type.ToString().ToLowerInvariant(), "--output", "json" };
        if (useSsl) arguments.Add("--use-ssl");
        if (request.IncludeHidden) arguments.Add("--include-hidden");
        if (request.IncludeInstalled) arguments.Add("--include-installed");
        if (!string.IsNullOrWhiteSpace(request.TitlePattern)) { arguments.Add("--title"); arguments.Add(request.TitlePattern); }
        foreach (var kb in request.KbArticleIds) { arguments.Add("--kb"); arguments.Add(kb); }
        var envelope = await RunAsync<List<UpdateRecord>>(arguments, cancellationToken).ConfigureAwait(false);
        return envelope.Data ?? new List<UpdateRecord>();
    }

    public static async Task<UpdateActionResult> ExecuteAsync(UpdateActionRequest request, string computerName, bool useSsl, CancellationToken cancellationToken)
    {
        var arguments = BuildExecuteArguments(request, computerName, useSsl);
        var envelope = await RunAsync<UpdateActionResult>(arguments, cancellationToken).ConfigureAwait(false);
        return envelope.Data ?? throw new InvalidDataException("The remote update operation returned no result data.");
    }

    internal static List<string> BuildExecuteArguments(UpdateActionRequest request, string computerName, bool useSsl)
    {
        var arguments = new List<string> { request.Action.ToString().ToLowerInvariant(), "--computer", computerName, "--source", FormatSource(request.Source), "--output", "json", "--yes" };
        if (useSsl) arguments.Add("--use-ssl");
        if (request.AcceptEulas) arguments.Add("--accept-eula");
        if (request.Force) arguments.Add("--force");
        if (request.PlanOnly) arguments.Add("--plan");
        foreach (var update in request.Updates) { arguments.Add("--update"); arguments.Add(update.ToString()); }
        return arguments;
    }

    public static async Task<UpdateSystemStatus> StatusAsync(string computerName, bool useSsl, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "status", "--computer", computerName, "--output", "json" };
        if (useSsl) arguments.Add("--use-ssl");
        var envelope = await RunAsync<UpdateSystemStatus>(arguments, cancellationToken).ConfigureAwait(false);
        return envelope.Data ?? throw new InvalidDataException("The remote status operation returned no data.");
    }

    public static async Task<IReadOnlyList<HistoryRecord>> HistoryAsync(string computerName, bool useSsl, int limit, CancellationToken cancellationToken)
    {
        var arguments = RemoteArguments("history", computerName, useSsl);
        arguments.Add("--limit");
        arguments.Add(limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        arguments.Add("--output");
        arguments.Add("json");
        var envelope = await RunAsync<List<HistoryRecord>>(arguments, cancellationToken).ConfigureAwait(false);
        return envelope.Data ?? new List<HistoryRecord>();
    }

    public static async Task<IReadOnlyList<UpdateServiceRecord>> ServicesAsync(string computerName, bool useSsl, CancellationToken cancellationToken)
    {
        var arguments = RemoteArguments("services", computerName, useSsl);
        arguments.Add("--output");
        arguments.Add("json");
        var envelope = await RunAsync<List<UpdateServiceRecord>>(arguments, cancellationToken).ConfigureAwait(false);
        return envelope.Data ?? new List<UpdateServiceRecord>();
    }

    public static async Task<string> AddMicrosoftUpdateServiceAsync(string computerName, bool useSsl, CancellationToken cancellationToken)
    {
        var arguments = RemoteArguments("services", computerName, useSsl);
        arguments.Insert(1, "add-microsoft-update");
        arguments.Add("--yes");
        arguments.Add("--output");
        arguments.Add("json");
        var envelope = await RunAsync<string>(arguments, cancellationToken).ConfigureAwait(false);
        return envelope.Data ?? "Microsoft Update registration completed.";
    }

    public static Task<string> PolicySetAsync(string computerName, bool useSsl, IEnumerable<string> changes, CancellationToken cancellationToken)
    {
        var arguments = RemoteArguments("policy", computerName, useSsl);
        arguments.Insert(1, "set");
        foreach (var change in changes)
        {
            arguments.Add("--value");
            arguments.Add(change);
        }
        arguments.Add("--yes");
        return RunTextAsync(arguments, cancellationToken);
    }

    public static Task<string> ResetComponentsAsync(string computerName, bool useSsl, CancellationToken cancellationToken)
    {
        var arguments = RemoteArguments("maintenance", computerName, useSsl);
        arguments.Insert(1, "reset-components");
        arguments.Add("--yes");
        return RunTextAsync(arguments, cancellationToken);
    }

    private static async Task<OperationEnvelope<T>> RunAsync<T>(IList<string> arguments, CancellationToken cancellationToken)
    {
        var capture = await RunProcessAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(capture.Output)) throw new InvalidOperationException(string.IsNullOrWhiteSpace(capture.Error) ? "The remote CLI produced no output." : capture.Error.Trim());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(capture.Output.Trim()));
        var serializer = new DataContractJsonSerializer(typeof(OperationEnvelope<T>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        var envelope = (OperationEnvelope<T>?)serializer.ReadObject(stream) ?? throw new InvalidDataException("The remote CLI response is empty.");
        if (envelope.Status == OperationState.Failed || envelope.Errors.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, envelope.Errors.Select(item => item.Message)));
        return envelope;
    }

    private static async Task<string> RunTextAsync(IList<string> arguments, CancellationToken cancellationToken)
    {
        var capture = await RunProcessAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (capture.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(capture.Error) ? capture.Output.Trim() : capture.Error.Trim());
        return capture.Output.Trim();
    }

    private static async Task<ProcessCapture> RunProcessAsync(IList<string> arguments, CancellationToken cancellationToken)
    {
        var executable = Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Could not locate the current executable.");
        var start = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the remote CLI worker.");
        using (cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } }))
        {
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessCapture(output, error, process.ExitCode);
        }
    }

    private static List<string> RemoteArguments(string command, string computerName, bool useSsl)
    {
        var arguments = new List<string> { command, "--computer", computerName };
        if (useSsl) arguments.Add("--use-ssl");
        return arguments;
    }

    private static string FormatSource(UpdateSourceKind source)
    {
        if (source == UpdateSourceKind.WindowsUpdate) return "windows-update";
        if (source == UpdateSourceKind.MicrosoftUpdate) return "microsoft-update";
        if (source == UpdateSourceKind.ManagedServer) return "managed-server";
        return source.ToString().ToLowerInvariant();
    }

    internal static string QuoteArgument(string value)
    {
        if (value.Length == 0) return "\"\"";
        var output = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '\"')
            {
                output.Append('\\', backslashes * 2 + 1).Append('\"');
                backslashes = 0;
                continue;
            }
            output.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        output.Append('\\', backslashes * 2).Append('\"');
        return output.ToString();
    }

    private sealed class ProcessCapture
    {
        public ProcessCapture(string output, string error, int exitCode) { Output = output; Error = error; ExitCode = exitCode; }
        public string Output { get; }
        public string Error { get; }
        public int ExitCode { get; }
    }
}
