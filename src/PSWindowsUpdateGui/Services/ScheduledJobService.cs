using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using PSWindowsUpdateGui.Cli;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Services;

[DataContract]
internal sealed class ScheduledUpdateManifest
{
    [DataMember(Name = "schemaVersion", Order = 1)] public int SchemaVersion { get; set; } = 1;
    [DataMember(Name = "jobId", Order = 2)] public string JobId { get; set; } = Guid.NewGuid().ToString("D");
    [DataMember(Name = "createdUtc", Order = 3)] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [DataMember(Name = "scheduledUtc", Order = 4)] public DateTime ScheduledUtc { get; set; }
    [DataMember(Name = "action", Order = 5)] public UpdateActionKind Action { get; set; }
    [DataMember(Name = "updates", Order = 6)] public IList<UpdateKey> Updates { get; set; } = new List<UpdateKey>();
    [DataMember(Name = "source", Order = 7)] public UpdateSourceKind Source { get; set; }
    [DataMember(Name = "serviceId", Order = 8)] public string ServiceId { get; set; } = string.Empty;
    [DataMember(Name = "acceptEulas", Order = 9)] public bool AcceptEulas { get; set; }
    [DataMember(Name = "executableHash", Order = 10)] public string ExecutableHash { get; set; } = string.Empty;
    [DataMember(Name = "state", Order = 11)] public string State { get; set; } = "Scheduled";
    [DataMember(Name = "lastResult", Order = 12)] public string LastResult { get; set; } = string.Empty;
}

internal sealed class ScheduledJobService
{
    private readonly string _jobRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PSWindowsUpdateGUI", "Jobs");

    public async Task<int> ExecuteCliAsync(string[] rawArguments, CliArguments arguments)
    {
        var verb = arguments.Positionals.Count > 1 ? arguments.Positionals[1].ToLowerInvariant() : "list";
        if (verb == "list") return List(arguments);
        if (verb == "create") return Create(arguments);
        if (verb == "run") return await RunAsync(arguments).ConfigureAwait(false);
        if (verb == "cancel") return Cancel(arguments);
        if (verb == "cleanup") return Cleanup(arguments);
        throw new FormatException("job supports create, list, run, cancel, or cleanup.");
    }

    private int List(CliArguments arguments)
    {
        EnsureRoot();
        var jobs = Directory.GetFiles(_jobRoot, "*.json").Select(Read).OrderBy(job => job.ScheduledUtc).ToList();
        if (IsJson(arguments)) WriteJson(jobs);
        else foreach (var job in jobs) Console.WriteLine($"{job.JobId}  {job.ScheduledUtc:u}  {job.State,-12} {job.Action} {job.Updates.Count} update(s)");
        return 0;
    }

    private int Create(CliArguments arguments)
    {
        var actionText = arguments.Get("action", "install");
        if (!Enum.TryParse(actionText, true, out UpdateActionKind action) || !Enum.IsDefined(typeof(UpdateActionKind), action) || action == UpdateActionKind.Hide || action == UpdateActionKind.Unhide)
            throw new FormatException("Scheduled action must be download, install, or uninstall.");
        if (!DateTimeOffset.TryParse(arguments.Get("at"), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var at))
            throw new FormatException("--at requires a local or offset-aware date/time.");
        if (at <= DateTimeOffset.Now.AddMinutes(1) || at > DateTimeOffset.Now.AddYears(1))
            throw new FormatException("The scheduled time must be at least one minute in the future and within one year.");
        var updates = arguments.GetAll("update").Select(UpdateKey.Parse).ToList();
        if (updates.Count == 0) throw new FormatException("At least one --update identity is required.");
        var plan = arguments.Has("plan");
        if (!plan && !arguments.Has("yes")) throw new InvalidOperationException("Creating a scheduled job noninteractively requires --yes.");

        var exe = Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Could not locate the current executable.");
        var manifest = new ScheduledUpdateManifest
        {
            ScheduledUtc = at.UtcDateTime,
            Action = action,
            Updates = updates,
            Source = ParseSource(arguments.Get("source", "default")),
            ServiceId = arguments.Get("service-id"),
            AcceptEulas = arguments.Has("accept-eula"),
            ExecutableHash = Hash(exe),
            State = plan ? "Planned" : "Scheduled"
        };
        if (plan)
        {
            Console.WriteLine($"Would create SYSTEM task PSWindowsUpdateGUI\\{manifest.JobId} for {at:u}.");
            return 0;
        }

        EnsureRoot();
        var path = Path.Combine(_jobRoot, manifest.JobId + ".json");
        Write(path, manifest);
        var taskName = @"PSWindowsUpdateGUI\" + manifest.JobId;
        var local = at.LocalDateTime;
        var taskXml = Path.Combine(_jobRoot, manifest.JobId + ".task.xml");
        try
        {
            File.WriteAllText(taskXml, BuildTaskXml(exe, path, local), Encoding.Unicode);
            RunSchtasks($"/Create /TN \"{taskName}\" /XML \"{taskXml}\" /F");
        }
        catch
        {
            try { File.Delete(path); } catch { }
            throw;
        }
        finally { try { File.Delete(taskXml); } catch { } }
        if (IsJson(arguments)) WriteJson(manifest); else Console.WriteLine($"Created job {manifest.JobId} for {at:u}.");
        return 0;
    }

    private async Task<int> RunAsync(CliArguments arguments)
    {
        var path = Path.GetFullPath(arguments.Get("manifest"));
        var root = Path.GetFullPath(_jobRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("The job manifest must be inside the app-owned job directory.");
        var manifest = Read(path);
        ValidateManifest(manifest, path);
        var exe = Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Could not locate the current executable.");
        if (!string.Equals(Hash(exe), manifest.ExecutableHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The scheduled executable does not match the manifest hash.");

        manifest.State = "Running"; Write(path, manifest);
        try
        {
            using var engine = new WuaWindowsUpdateEngine();
            var result = await engine.ExecuteAsync(new UpdateActionRequest
            {
                Action = manifest.Action,
                Updates = manifest.Updates,
                Source = manifest.Source,
                ServiceId = manifest.ServiceId,
                AcceptEulas = manifest.AcceptEulas
            }, null, CancellationToken.None).ConfigureAwait(false);
            manifest.State = result.RebootRequired ? "RebootRequired" : "Completed";
            manifest.LastResult = result.Result;
            Write(path, manifest);
            if (IsJson(arguments)) WriteJson(result); else Console.WriteLine($"Scheduled job {manifest.JobId}: {result.Result}");
            return result.RebootRequired ? 3010 : 0;
        }
        catch (Exception exception)
        {
            manifest.State = "Failed"; manifest.LastResult = exception.Message; Write(path, manifest); throw;
        }
    }

    private int Cancel(CliArguments arguments)
    {
        var id = RequireJobId(arguments.Get("id"));
        if (!arguments.Has("yes")) throw new InvalidOperationException("Cancelling a scheduled job noninteractively requires --yes.");
        RunSchtasks($"/Delete /TN \"PSWindowsUpdateGUI\\{id}\" /F", false);
        var path = Path.Combine(_jobRoot, id + ".json");
        if (File.Exists(path)) { var job = Read(path); job.State = "Cancelled"; Write(path, job); }
        Console.WriteLine($"Cancelled scheduled job {id}.");
        return 0;
    }

    private int Cleanup(CliArguments arguments)
    {
        if (!arguments.Has("yes")) throw new InvalidOperationException("Job cleanup noninteractively requires --yes.");
        EnsureRoot();
        var removed = 0;
        foreach (var path in Directory.GetFiles(_jobRoot, "*.json"))
        {
            var job = Read(path);
            if (job.State == "Running" || job.State == "Scheduled") continue;
            RunSchtasks($"/Delete /TN \"PSWindowsUpdateGUI\\{job.JobId}\" /F", false);
            File.Delete(path); removed++;
        }
        Console.WriteLine($"Removed {removed} completed/cancelled job manifest(s).");
        return 0;
    }

    private void EnsureRoot()
    {
        Directory.CreateDirectory(_jobRoot);
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(_jobRoot, security);
        }
        catch (PlatformNotSupportedException) { }
    }

    private static void RunSchtasks(string arguments, bool throwOnError = true)
    {
        using var process = Process.Start(new ProcessStartInfo { FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"), Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true })
                            ?? throw new InvalidOperationException("Could not start Task Scheduler command.");
        var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (throwOnError && process.ExitCode != 0) throw new InvalidOperationException((error + Environment.NewLine + output).Trim());
    }

    internal static string BuildTaskXml(string executable, string manifestPath, DateTime localTime)
    {
        var command = SecurityElement.Escape(Path.GetFullPath(executable)) ?? throw new InvalidOperationException("Could not encode the scheduled executable path.");
        var workingDirectory = SecurityElement.Escape(Path.GetDirectoryName(Path.GetFullPath(executable)) ?? string.Empty) ?? string.Empty;
        var arguments = SecurityElement.Escape("job run --manifest \"" + Path.GetFullPath(manifestPath) + "\" --yes") ?? throw new InvalidOperationException("Could not encode the scheduled arguments.");
        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Author>PSWindowsUpdateGUI</Author><Description>Schema-validated Windows Update operation</Description></RegistrationInfo>
  <Triggers><TimeTrigger><StartBoundary>{localTime:yyyy-MM-ddTHH:mm:ss}</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>S-1-5-18</UserId><LogonType>ServiceAccount</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>false</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT12H</ExecutionTimeLimit><Priority>7</Priority></Settings>
  <Actions Context=""Author""><Exec><Command>{command}</Command><Arguments>{arguments}</Arguments><WorkingDirectory>{workingDirectory}</WorkingDirectory></Exec></Actions>
</Task>";
    }

    private static UpdateSourceKind ParseSource(string value)
    {
        var normalized = value.Replace("-", string.Empty).Replace(" ", string.Empty);
        if (!Enum.TryParse(normalized, true, out UpdateSourceKind source) || !Enum.IsDefined(typeof(UpdateSourceKind), source) || source == UpdateSourceKind.Offline) throw new FormatException("Unsupported scheduled update source.");
        return source;
    }

    private static void ValidateManifest(ScheduledUpdateManifest manifest, string path)
    {
        if (manifest.SchemaVersion != 1 || !Guid.TryParse(manifest.JobId, out var jobId) ||
            !string.Equals(Path.GetFileNameWithoutExtension(path), jobId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The scheduled job identity or schema is invalid.");
        if (!Enum.IsDefined(typeof(UpdateActionKind), manifest.Action) ||
            (manifest.Action != UpdateActionKind.Download && manifest.Action != UpdateActionKind.Install && manifest.Action != UpdateActionKind.Uninstall))
            throw new InvalidDataException("The scheduled action is not allowed.");
        if (!Enum.IsDefined(typeof(UpdateSourceKind), manifest.Source) || manifest.Source == UpdateSourceKind.Offline)
            throw new InvalidDataException("The scheduled source is not allowed.");
        if (manifest.Source == UpdateSourceKind.Service && !Guid.TryParse(manifest.ServiceId, out _))
            throw new InvalidDataException("The scheduled service identity is invalid.");
        if (manifest.Updates == null || manifest.Updates.Count == 0 || manifest.Updates.Count > 1000 || manifest.Updates.Distinct().Count() != manifest.Updates.Count)
            throw new InvalidDataException("The scheduled update identity list is invalid.");
        if (!Regex.IsMatch(manifest.ExecutableHash ?? string.Empty, "^[A-Fa-f0-9]{64}$"))
            throw new InvalidDataException("The scheduled executable hash is invalid.");
    }

    private static string RequireJobId(string value) => Guid.TryParse(value, out var id) ? id.ToString("D") : throw new FormatException("--id must be a job GUID.");
    private static bool IsJson(CliArguments args) => args.Get("output").StartsWith("json", StringComparison.OrdinalIgnoreCase);
    private static string Hash(string path) { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty); }
    private static ScheduledUpdateManifest Read(string path) { using var stream = File.OpenRead(path); return (ScheduledUpdateManifest?)new DataContractJsonSerializer(typeof(ScheduledUpdateManifest)).ReadObject(stream) ?? throw new InvalidDataException("Empty job manifest."); }
    private static void Write(string path, ScheduledUpdateManifest job) { var temporary = path + ".tmp"; using (var stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(ScheduledUpdateManifest)).WriteObject(stream, job); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
    private static void WriteJson<T>(T value) { using var stream = new MemoryStream(); new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray())); }
}
