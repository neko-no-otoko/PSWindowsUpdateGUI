using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;

namespace PSWindowsUpdateGui.Models;

internal sealed class CommandDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Synopsis { get; set; } = string.Empty;

    public bool SupportsShouldProcess { get; set; }

    public IList<ParameterSetDefinition> ParameterSets { get; } = new List<ParameterSetDefinition>();

    public bool SupportsRemote => ParameterSets.Any(set => set.Parameters.Any(parameter => parameter.Name == "ComputerName"));

    public override string ToString() => Name;
}

internal sealed class ParameterSetDefinition
{
    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public IList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>();

    public override string ToString() => IsDefault ? $"{Name} (default)" : Name;
}

internal sealed class ParameterDefinition
{
    public string Name { get; set; } = string.Empty;

    public Type ParameterType { get; set; } = typeof(string);

    public bool IsMandatory { get; set; }

    public IList<string> ValidValues { get; } = new List<string>();

    public object? Minimum { get; set; }

    public object? Maximum { get; set; }

    public bool IsSwitch => ParameterType == typeof(SwitchParameter) || ParameterType == typeof(bool);

    public bool IsCredential => ParameterType == typeof(PSCredential);

    public bool IsArray => ParameterType.IsArray;

    public string TypeLabel => ParameterType.Name;
}

internal static class CommandRegistry
{
    public static readonly string[] PublicCommands =
    {
        "Add-WUServiceManager",
        "Enable-WURemoting",
        "Get-WindowsUpdate",
        "Get-WUApiVersion",
        "Get-WUHistory",
        "Get-WUInstallerStatus",
        "Get-WUJob",
        "Get-WULastResults",
        "Get-WUOfflineMSU",
        "Get-WURebootStatus",
        "Get-WUServiceManager",
        "Get-WUSettings",
        "Invoke-WUJob",
        "Remove-WindowsUpdate",
        "Remove-WUServiceManager",
        "Reset-WUComponents",
        "Set-PSWUSettings",
        "Set-WUSettings",
        "Update-WUModule"
    };

    public static readonly ISet<string> CommonParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Debug", "ErrorAction", "ErrorVariable", "InformationAction", "InformationVariable",
        "OutBuffer", "OutVariable", "PipelineVariable", "Verbose", "WarningAction", "WarningVariable",
        "WhatIf", "Confirm"
    };

    public static bool IsAllowed(string command) =>
        PublicCommands.Contains(command, StringComparer.OrdinalIgnoreCase);
}
