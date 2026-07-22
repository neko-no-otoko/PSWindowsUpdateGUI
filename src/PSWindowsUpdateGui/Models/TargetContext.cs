namespace PSWindowsUpdateGui.Models;

internal sealed class TargetContext
{
    public bool IsRemote { get; set; }

    public string ComputerName { get; set; } = string.Empty;

    public string DisplayName => IsRemote ? ComputerName : "This computer";
}
