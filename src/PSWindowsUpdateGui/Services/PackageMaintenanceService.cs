using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PSWindowsUpdateGui.Services;

internal sealed class PackageMaintenanceService
{
    private static readonly Regex KbPattern = new Regex("^(?:KB)?([0-9]{6,8})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<IReadOnlyList<string>> UninstallAsync(string packagePath, string kbArticle, bool planOnly, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            string executable;
            string arguments;
            var summary = new List<string>();
            if (!string.IsNullOrWhiteSpace(packagePath))
            {
                var path = Path.GetFullPath(packagePath);
                if (!File.Exists(path)) throw new FileNotFoundException("The package was not found.", path);
                var extension = Path.GetExtension(path);
                if (extension.Equals(".msu", StringComparison.OrdinalIgnoreCase))
                {
                    executable = Path.Combine(Environment.SystemDirectory, "wusa.exe");
                    arguments = Quote(path) + " /uninstall /quiet /norestart";
                }
                else if (extension.Equals(".cab", StringComparison.OrdinalIgnoreCase))
                {
                    executable = Path.Combine(Environment.SystemDirectory, "dism.exe");
                    arguments = "/Online /Remove-Package /PackagePath:" + Quote(path) + " /NoRestart";
                }
                else throw new FormatException("Explicit package uninstall supports only .msu and .cab files.");
                summary.Add($"Explicit package: {path}");
            }
            else
            {
                var match = KbPattern.Match(kbArticle ?? string.Empty);
                if (!match.Success) throw new FormatException("Explicit WUSA uninstall requires --kb followed by a 6-8 digit KB number.");
                executable = Path.Combine(Environment.SystemDirectory, "wusa.exe");
                arguments = "/uninstall /kb:" + match.Groups[1].Value + " /quiet /norestart";
                summary.Add("Explicit KB: KB" + match.Groups[1].Value);
            }
            summary.Add("Automatic restart is disabled.");
            if (planOnly) { summary.Insert(0, "Planned package uninstall; no process was started."); return summary; }

            using var operationLock = MachineMutationLock.Acquire();
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo { FileName = executable, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true })
                                ?? throw new InvalidOperationException("Could not start the Windows package servicing process.");
            while (!process.WaitForExit(500))
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("Package servicing is still running; monitoring stopped without terminating the servicing process.", cancellationToken);
            }
            summary.Add("Exit code: " + process.ExitCode);
            if (process.ExitCode != 0 && process.ExitCode != 3010) throw new InvalidOperationException($"Package servicing failed with exit code {process.ExitCode}.");
            return summary;
        }, cancellationToken);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
