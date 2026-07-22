using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace PSWindowsUpdateGui.Services;

[DataContract]
internal sealed class PolicySnapshot
{
    [DataMember(Name = "schemaVersion", Order = 1)] public int SchemaVersion { get; set; } = 1;
    [DataMember(Name = "createdUtc", Order = 2)] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [DataMember(Name = "values", Order = 3)] public IDictionary<string, string?> Values { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

internal sealed class WindowsUpdatePolicyService
{
    private const string WindowsUpdatePath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string AutomaticUpdatesPath = WindowsUpdatePath + @"\AU";

    private static readonly IDictionary<string, PolicyDefinition> Definitions =
        new Dictionary<string, PolicyDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["WUServer"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.String),
            ["WUStatusServer"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.String),
            ["UpdateServiceUrlAlternate"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.String),
            ["TargetGroup"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.String),
            ["TargetGroupEnabled"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 1),
            ["DisableWindowsUpdateAccess"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 1),
            ["DoNotConnectToWindowsUpdateInternetLocations"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 1),
            ["SetDisableUXWUAccess"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 1),
            ["DeferFeatureUpdates"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 1),
            ["DeferFeatureUpdatesPeriodInDays"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 365),
            ["DeferQualityUpdates"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 1),
            ["DeferQualityUpdatesPeriodInDays"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 30),
            ["BranchReadinessLevel"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 2, 32),
            ["ManagePreviewBuilds"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 2),
            ["AllowOptionalContent"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 3),
            ["AcceptTrustedPublisherCert"] = new PolicyDefinition(WindowsUpdatePath, RegistryValueKind.DWord, 0, 1),
            ["NoAutoUpdate"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 1),
            ["AUOptions"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 2, 7),
            ["ScheduledInstallDay"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 7),
            ["ScheduledInstallTime"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 23),
            ["UseWUServer"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 1),
            ["AutoInstallMinorUpdates"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 1),
            ["DetectionFrequencyEnabled"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 1),
            ["DetectionFrequency"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 1, 22),
            ["NoAutoRebootWithLoggedOnUsers"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 1),
            ["RebootWarningTimeoutEnabled"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 1),
            ["RebootWarningTimeout"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 1, 30),
            ["RebootRelaunchTimeoutEnabled"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 1),
            ["RebootRelaunchTimeout"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 1, 1440),
            ["RescheduleWaitTimeEnabled"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 0, 1),
            ["RescheduleWaitTime"] = new PolicyDefinition(AutomaticUpdatesPath, RegistryValueKind.DWord, 1, 60)
        };

    public PolicySnapshot Read()
    {
        var snapshot = new PolicySnapshot();
        foreach (var pair in Definitions)
        {
            using var key = Registry.LocalMachine.OpenSubKey(pair.Value.Path, false);
            var value = key?.GetValue(pair.Key, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            snapshot.Values[pair.Key] = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        return snapshot;
    }

    public string Render() => string.Join(Environment.NewLine,
        Read().Values.Select(pair => $"{pair.Key}={(pair.Value ?? "<not configured>")}"));

    public string Preview(IList<string> changes)
    {
        var parsed = Parse(changes);
        return "Windows Update policy changes:" + Environment.NewLine +
               string.Join(Environment.NewLine, parsed.Select(pair => $"  {pair.Key} = {(pair.Value ?? "<remove>")}"));
    }

    public string Backup(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"WindowsUpdatePolicy-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        using var stream = File.Create(path);
        new DataContractJsonSerializer(typeof(PolicySnapshot), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true })
            .WriteObject(stream, Read());
        return path;
    }

    public void Apply(IList<string> changes)
    {
        var parsed = Parse(changes);
        foreach (var pair in parsed)
        {
            var definition = Definitions[pair.Key];
            using var key = Registry.LocalMachine.CreateSubKey(definition.Path, true) ?? throw new InvalidOperationException("Could not open the Windows Update policy key.");
            if (pair.Value == null) key.DeleteValue(pair.Key, false);
            else key.SetValue(pair.Key, ConvertValue(pair.Value, definition), definition.Kind);
        }
    }

    public void Restore(string backupPath)
    {
        using var stream = File.OpenRead(Path.GetFullPath(backupPath));
        var snapshot = (PolicySnapshot?)new DataContractJsonSerializer(typeof(PolicySnapshot), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true })
            .ReadObject(stream) ?? throw new InvalidDataException("The policy backup is empty.");
        if (snapshot.SchemaVersion != 1) throw new InvalidDataException("Unsupported policy backup schema.");
        var changes = snapshot.Values.Where(pair => Definitions.ContainsKey(pair.Key))
            .Select(pair => pair.Key + "=" + (pair.Value ?? "remove")).ToList();
        Apply(changes);
    }

    private static IDictionary<string, string?> Parse(IList<string> changes)
    {
        if (changes == null || changes.Count == 0) throw new FormatException("At least one --value Name=Value is required.");
        var output = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in changes)
        {
            var separator = change.IndexOf('=');
            if (separator < 1) throw new FormatException("Policy values must use Name=Value.");
            var name = change.Substring(0, separator).Trim();
            var value = change.Substring(separator + 1).Trim();
            if (!Definitions.TryGetValue(name, out var definition)) throw new FormatException($"Unsupported Windows Update policy value: {name}.");
            if (value.Equals("remove", StringComparison.OrdinalIgnoreCase) || value.Equals("null", StringComparison.OrdinalIgnoreCase)) output[name] = null;
            else
            {
                _ = ConvertValue(value, definition);
                output[name] = value;
            }
        }
        return output;
    }

    private static object ConvertValue(string value, PolicyDefinition definition)
    {
        if (definition.Kind == RegistryValueKind.String)
        {
            if (value.Length > 2048 || value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) throw new FormatException("Policy strings must be a single line of at most 2,048 characters.");
            return value;
        }
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw new FormatException("The policy value must be an integer.");
        if (number < definition.Minimum || number > definition.Maximum) throw new FormatException($"The policy value must be from {definition.Minimum} through {definition.Maximum}.");
        return number;
    }

    private sealed class PolicyDefinition
    {
        public PolicyDefinition(string path, RegistryValueKind kind, int minimum = int.MinValue, int maximum = int.MaxValue)
        {
            Path = path; Kind = kind; Minimum = minimum; Maximum = maximum;
        }
        public string Path { get; }
        public RegistryValueKind Kind { get; }
        public int Minimum { get; }
        public int Maximum { get; }
    }
}
