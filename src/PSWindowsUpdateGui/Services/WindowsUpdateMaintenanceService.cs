using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace PSWindowsUpdateGui.Services;

internal sealed class WindowsUpdateMaintenanceService
{
    private static readonly string[] ServiceNames = { "bits", "wuauserv", "cryptsvc" };

    public Task<IReadOnlyList<string>> ResetAsync(bool planOnly, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<string>>(() =>
    {
        var output = new List<string>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var targets = new[]
        {
            Path.Combine(windows, "SoftwareDistribution"),
            Path.Combine(windows, "System32", "catroot2")
        };
        if (planOnly)
        {
            output.Add("Would stop BITS, Windows Update, and Cryptographic Services.");
            foreach (var target in targets) output.Add($"Would preserve-reset {target} by renaming it to a timestamped backup.");
            output.Add("Would restart the services that were running before the reset.");
            return output;
        }

        using var operationLock = MachineMutationLock.Acquire();

        var previouslyRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var name in ServiceNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var service = new ServiceController(name);
                if (service.Status == ServiceControllerStatus.Running)
                {
                    previouslyRunning.Add(name);
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                }
                output.Add($"Stopped {name}.");
            }

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(target)) continue;
                var backup = target + $".PSWindowsUpdateGUI.{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                Directory.Move(target, backup);
                output.Add($"Renamed {target} to {backup}.");
            }
        }
        finally
        {
            foreach (var name in previouslyRunning)
            {
                try
                {
                    using var service = new ServiceController(name);
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
                    output.Add($"Restarted {name}.");
                }
                catch (Exception exception) { output.Add($"WARNING: Could not restart {name}: {exception.Message}"); }
            }
        }
        return output;
    }, cancellationToken);
}
