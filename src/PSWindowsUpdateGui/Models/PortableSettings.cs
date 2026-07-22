using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PSWindowsUpdateGui.Models;

[DataContract]
internal sealed class PortableSettings
{
    [DataMember(Name = "schemaVersion", Order = 1)]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Name = "recentComputers", Order = 2)]
    public List<string> RecentComputers { get; set; } = new List<string>();

    [DataMember(Name = "lastUpdateSource", Order = 3)]
    public string LastUpdateSource { get; set; } = "Default";

    [DataMember(Name = "logRetentionDays", Order = 4)]
    public int LogRetentionDays { get; set; } = 30;

    [DataMember(Name = "logSizeLimitMb", Order = 5)]
    public int LogSizeLimitMb { get; set; } = 25;
}
