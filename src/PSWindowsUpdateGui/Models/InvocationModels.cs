using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace PSWindowsUpdateGui.Models;

internal enum InvocationEventKind
{
    Output,
    Progress,
    Verbose,
    Warning,
    Information,
    Error
}

internal sealed class InvocationEvent : EventArgs
{
    public InvocationEvent(InvocationEventKind kind, string message, int? percentComplete = null)
    {
        Kind = kind;
        Message = message;
        PercentComplete = percentComplete;
    }

    public InvocationEventKind Kind { get; }

    public string Message { get; }

    public int? PercentComplete { get; }
}

internal sealed class InvocationResult
{
    public IList<PSObject> Output { get; } = new List<PSObject>();

    public IList<string> Errors { get; } = new List<string>();

    public bool WasCancelled { get; set; }

    public bool Succeeded => !WasCancelled && Errors.Count == 0;
}
