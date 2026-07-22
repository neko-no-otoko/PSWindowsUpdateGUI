using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PSWindowsUpdateGui.Models;

internal enum UpdateSourceKind
{
    Default,
    WindowsUpdate,
    MicrosoftUpdate,
    ManagedServer,
    Service,
    Offline
}

internal enum UpdateKind
{
    All,
    Software,
    Driver
}

internal enum UpdateActionKind
{
    Download,
    Install,
    Uninstall,
    Hide,
    Unhide
}

internal enum OperationState
{
    Success,
    Partial,
    Failed,
    Cancelled,
    MonitoringStopped,
    RebootRequired,
    Planned
}

[DataContract]
internal sealed class UpdateKey : IEquatable<UpdateKey>
{
    [DataMember(Name = "updateId", Order = 1)]
    public string UpdateId { get; set; } = string.Empty;

    [DataMember(Name = "revision", Order = 2)]
    public int Revision { get; set; }

    public override string ToString() => $"{UpdateId}:{Revision}";

    public bool Equals(UpdateKey? other) => other != null &&
        Revision == other.Revision &&
        string.Equals(UpdateId, other.UpdateId, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as UpdateKey);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(UpdateId) ^ Revision;

    public static UpdateKey Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("An update identity is required.");
        var separator = value.LastIndexOf(':');
        if (separator < 1 || !Guid.TryParse(value.Substring(0, separator), out var id) ||
            !int.TryParse(value.Substring(separator + 1), out var revision) || revision < 0)
        {
            throw new FormatException("Update identities must use <guid>:<revision>.");
        }

        return new UpdateKey { UpdateId = id.ToString("D"), Revision = revision };
    }
}

[DataContract]
internal sealed class UpdateRecord
{
    [IgnoreDataMember]
    public bool IsSelected { get; set; }

    [DataMember(Name = "identity", Order = 1)]
    public UpdateKey Identity { get; set; } = new UpdateKey();

    [DataMember(Name = "title", Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Name = "description", Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Name = "type", Order = 4)]
    public string Type { get; set; } = string.Empty;

    [DataMember(Name = "kbArticleIds", Order = 5)]
    public IList<string> KbArticleIds { get; set; } = new List<string>();

    [DataMember(Name = "categories", Order = 6)]
    public IList<string> Categories { get; set; } = new List<string>();

    [DataMember(Name = "minimumDownloadBytes", Order = 7)]
    public decimal MinimumDownloadBytes { get; set; }

    [DataMember(Name = "maximumDownloadBytes", Order = 8)]
    public decimal MaximumDownloadBytes { get; set; }

    [DataMember(Name = "isDownloaded", Order = 9)]
    public bool IsDownloaded { get; set; }

    [DataMember(Name = "isInstalled", Order = 10)]
    public bool IsInstalled { get; set; }

    [DataMember(Name = "isHidden", Order = 11)]
    public bool IsHidden { get; set; }

    [DataMember(Name = "isUninstallable", Order = 12)]
    public bool IsUninstallable { get; set; }

    [DataMember(Name = "eulaAccepted", Order = 13)]
    public bool EulaAccepted { get; set; }

    [DataMember(Name = "rebootMayBeRequired", Order = 14)]
    public bool RebootMayBeRequired { get; set; }

    [DataMember(Name = "severity", Order = 15)]
    public string Severity { get; set; } = string.Empty;

    [DataMember(Name = "driverProvider", Order = 16)]
    public string DriverProvider { get; set; } = string.Empty;

    [DataMember(Name = "driverManufacturer", Order = 17)]
    public string DriverManufacturer { get; set; } = string.Empty;

    [DataMember(Name = "driverClass", Order = 18)]
    public string DriverClass { get; set; } = string.Empty;

    [DataMember(Name = "driverModel", Order = 19)]
    public string DriverModel { get; set; } = string.Empty;

    [DataMember(Name = "driverVersion", Order = 20)]
    public string DriverVersion { get; set; } = string.Empty;

    [DataMember(Name = "driverDateUtc", Order = 21, EmitDefaultValue = false)]
    public DateTime? DriverDateUtc { get; set; }

    [IgnoreDataMember]
    public string Kb => KbArticleIds.Count == 0 ? string.Empty : "KB" + string.Join(", KB", KbArticleIds);

    [IgnoreDataMember]
    public string Size => MaximumDownloadBytes >= 1024m * 1024m * 1024m
        ? $"{MaximumDownloadBytes / 1024m / 1024m / 1024m:N1} GiB"
        : $"{MaximumDownloadBytes / 1024m / 1024m:N1} MiB";
}

[DataContract]
internal sealed class ScanRequest
{
    [DataMember(Name = "source", Order = 1)]
    public UpdateSourceKind Source { get; set; } = UpdateSourceKind.Default;

    [DataMember(Name = "type", Order = 2)]
    public UpdateKind Type { get; set; } = UpdateKind.All;

    [DataMember(Name = "includeInstalled", Order = 3)]
    public bool IncludeInstalled { get; set; }

    [DataMember(Name = "includeHidden", Order = 4)]
    public bool IncludeHidden { get; set; }

    [DataMember(Name = "titlePattern", Order = 5)]
    public string TitlePattern { get; set; } = string.Empty;

    [DataMember(Name = "kbArticleIds", Order = 6)]
    public IList<string> KbArticleIds { get; set; } = new List<string>();

    [DataMember(Name = "criteria", Order = 7)]
    public string Criteria { get; set; } = string.Empty;

    [DataMember(Name = "serviceId", Order = 8)]
    public string ServiceId { get; set; } = string.Empty;

    [DataMember(Name = "offlineCabPath", Order = 9)]
    public string OfflineCabPath { get; set; } = string.Empty;

    [DataMember(Name = "timeoutSeconds", Order = 10)]
    public int TimeoutSeconds { get; set; } = 1800;
}

[DataContract]
internal sealed class UpdateActionRequest
{
    [DataMember(Name = "action", Order = 1)]
    public UpdateActionKind Action { get; set; }

    [DataMember(Name = "updates", Order = 2)]
    public IList<UpdateKey> Updates { get; set; } = new List<UpdateKey>();

    [DataMember(Name = "source", Order = 3)]
    public UpdateSourceKind Source { get; set; } = UpdateSourceKind.Default;

    [DataMember(Name = "serviceId", Order = 4)]
    public string ServiceId { get; set; } = string.Empty;

    [DataMember(Name = "acceptEulas", Order = 5)]
    public bool AcceptEulas { get; set; }

    [DataMember(Name = "force", Order = 6)]
    public bool Force { get; set; }

    [DataMember(Name = "planOnly", Order = 7)]
    public bool PlanOnly { get; set; }

    [DataMember(Name = "timeoutSeconds", Order = 8)]
    public int TimeoutSeconds { get; set; } = 7200;
}

[DataContract]
internal sealed class UpdateItemResult
{
    [DataMember(Name = "identity", Order = 1)]
    public UpdateKey Identity { get; set; } = new UpdateKey();

    [DataMember(Name = "title", Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Name = "result", Order = 3)]
    public string Result { get; set; } = string.Empty;

    [DataMember(Name = "hresult", Order = 4)]
    public int HResult { get; set; }
}

[DataContract]
internal sealed class UpdateActionResult
{
    [DataMember(Name = "action", Order = 1)]
    public UpdateActionKind Action { get; set; }

    [DataMember(Name = "result", Order = 2)]
    public string Result { get; set; } = string.Empty;

    [DataMember(Name = "hresult", Order = 3)]
    public int HResult { get; set; }

    [DataMember(Name = "rebootRequired", Order = 4)]
    public bool RebootRequired { get; set; }

    [DataMember(Name = "updates", Order = 5)]
    public IList<UpdateItemResult> Updates { get; set; } = new List<UpdateItemResult>();
}

[DataContract]
internal sealed class HistoryRecord
{
    [DataMember(Name = "dateUtc", Order = 1)] public DateTime DateUtc { get; set; }
    [DataMember(Name = "title", Order = 2)] public string Title { get; set; } = string.Empty;
    [DataMember(Name = "operation", Order = 3)] public string Operation { get; set; } = string.Empty;
    [DataMember(Name = "result", Order = 4)] public string Result { get; set; } = string.Empty;
    [DataMember(Name = "hresult", Order = 5)] public int HResult { get; set; }
    [DataMember(Name = "client", Order = 6)] public string Client { get; set; } = string.Empty;
    [DataMember(Name = "identity", Order = 7)] public UpdateKey Identity { get; set; } = new UpdateKey();
}

[DataContract]
internal sealed class UpdateSystemStatus
{
    [DataMember(Name = "computerName", Order = 1)] public string ComputerName { get; set; } = string.Empty;
    [DataMember(Name = "agentVersion", Order = 2)] public string AgentVersion { get; set; } = string.Empty;
    [DataMember(Name = "rebootRequired", Order = 3)] public bool RebootRequired { get; set; }
    [DataMember(Name = "installerBusy", Order = 4)] public bool InstallerBusy { get; set; }
    [DataMember(Name = "updateServiceStatus", Order = 5)] public string UpdateServiceStatus { get; set; } = string.Empty;
    [DataMember(Name = "orchestratorNotice", Order = 6)] public string OrchestratorNotice { get; set; } = string.Empty;
    [DataMember(Name = "localTime", Order = 7)] public DateTimeOffset LocalTime { get; set; }
}

[DataContract]
internal sealed class OperationError
{
    [DataMember(Name = "code", Order = 1)] public string Code { get; set; } = string.Empty;
    [DataMember(Name = "hresult", Order = 2)] public int HResult { get; set; }
    [DataMember(Name = "message", Order = 3)] public string Message { get; set; } = string.Empty;
}

[DataContract]
internal sealed class OperationEnvelope<T>
{
    [DataMember(Name = "schemaVersion", Order = 1)] public int SchemaVersion { get; set; } = 1;
    [DataMember(Name = "operationId", Order = 2)] public string OperationId { get; set; } = Guid.NewGuid().ToString("D");
    [DataMember(Name = "target", Order = 3)] public string Target { get; set; } = Environment.MachineName;
    [DataMember(Name = "startedUtc", Order = 4)] public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    [DataMember(Name = "completedUtc", Order = 5)] public DateTime CompletedUtc { get; set; }
    [DataMember(Name = "status", Order = 6)] public OperationState Status { get; set; }
    [DataMember(Name = "warnings", Order = 7)] public IList<string> Warnings { get; set; } = new List<string>();
    [DataMember(Name = "errors", Order = 8)] public IList<OperationError> Errors { get; set; } = new List<OperationError>();
    [DataMember(Name = "data", Order = 9, EmitDefaultValue = false)] public T? Data { get; set; }
}

internal sealed class UpdateProgress
{
    public UpdateProgress(string activity, int percentComplete, int currentIndex = -1)
    {
        Activity = activity;
        PercentComplete = percentComplete;
        CurrentIndex = currentIndex;
    }

    public string Activity { get; }
    public int PercentComplete { get; }
    public int CurrentIndex { get; }
}

internal interface IWindowsUpdateEngine : IDisposable
{
    System.Threading.Tasks.Task<IReadOnlyList<UpdateRecord>> ScanAsync(ScanRequest request, IProgress<UpdateProgress>? progress, System.Threading.CancellationToken cancellationToken);
    System.Threading.Tasks.Task<UpdateActionResult> ExecuteAsync(UpdateActionRequest request, IProgress<UpdateProgress>? progress, System.Threading.CancellationToken cancellationToken);
    System.Threading.Tasks.Task<IReadOnlyList<HistoryRecord>> GetHistoryAsync(int limit, System.Threading.CancellationToken cancellationToken);
    System.Threading.Tasks.Task<UpdateSystemStatus> GetStatusAsync(System.Threading.CancellationToken cancellationToken);
    System.Threading.Tasks.Task<IReadOnlyList<UpdateServiceRecord>> GetServicesAsync(System.Threading.CancellationToken cancellationToken);
    System.Threading.Tasks.Task<string> AddMicrosoftUpdateServiceAsync(bool planOnly, System.Threading.CancellationToken cancellationToken);
    System.Threading.Tasks.Task RemoveServiceAsync(string serviceId, bool planOnly, System.Threading.CancellationToken cancellationToken);
    System.Threading.Tasks.Task ExportPayloadsAsync(IList<UpdateKey> updates, string destination, System.Threading.CancellationToken cancellationToken);
}

[DataContract]
internal sealed class UpdateServiceRecord
{
    [DataMember(Name = "serviceId", Order = 1)] public string ServiceId { get; set; } = string.Empty;
    [DataMember(Name = "name", Order = 2)] public string Name { get; set; } = string.Empty;
    [DataMember(Name = "managed", Order = 3)] public bool IsManaged { get; set; }
    [DataMember(Name = "registeredWithAutomaticUpdates", Order = 4)] public bool IsRegisteredWithAutomaticUpdates { get; set; }
    [DataMember(Name = "scanPackage", Order = 5)] public bool IsScanPackage { get; set; }
}
