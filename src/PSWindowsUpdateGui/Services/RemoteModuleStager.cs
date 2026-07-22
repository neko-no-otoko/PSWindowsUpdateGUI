using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PSWindowsUpdateGui.Services;

internal sealed class RemoteModuleStager
{
    private const string OwnerMarker = ".pswugui-owned";
    private readonly ModuleRuntime _module;
    private readonly PortableLogService _log;

    public RemoteModuleStager(ModuleRuntime module, PortableLogService log)
    {
        _module = module;
        _log = log;
    }

    public RemoteStageResult EnsureAvailable(string computerName)
    {
        ValidateComputerName(computerName);
        var target = GetTargetPath(computerName);
        var targetDll = Path.Combine(target, "PSWindowsUpdate.dll");
        if (File.Exists(targetDll))
        {
            foreach (var file in _module.Manifest.Files.Where(file => !file.Path.StartsWith("[", StringComparison.Ordinal)))
            {
                var existing = Path.Combine(target, file.Path);
                if (!File.Exists(existing) || !string.Equals(Hash(existing), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"The existing remote PSWindowsUpdate 2.2.1.5 file {file.Path} does not match the pinned package. It was not changed.");
                }
            }

            var marker = Path.Combine(target, OwnerMarker);
            if (File.Exists(marker))
            {
                var existingToken = File.ReadAllText(marker).Trim();
                if (Guid.TryParseExact(existingToken, "N", out _))
                {
                    _log.Write("Remote", $"Reconciled an app-owned PSWindowsUpdate 2.2.1.5 copy on {computerName}.");
                    return new RemoteStageResult(target, true, existingToken);
                }

                throw new InvalidDataException("The remote ownership marker is invalid. The existing module was not changed.");
            }

            return new RemoteStageResult(target, false, string.Empty);
        }

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new IOException("The target module version directory already exists and is not empty. It was not changed.");
        }

        Directory.CreateDirectory(target);
        var token = Guid.NewGuid().ToString("N");
        try
        {
            foreach (var file in _module.Manifest.Files.Where(file => !file.Path.StartsWith("[", StringComparison.Ordinal)))
            {
                var source = Path.Combine(_module.ModuleDirectory, file.Path);
                var destination = Path.Combine(target, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? target);
                File.Copy(source, destination, false);
                if (!string.Equals(Hash(destination), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Remote integrity verification failed for {file.Path}.");
                }
            }

            File.WriteAllText(Path.Combine(target, OwnerMarker), token);
            _log.Write("Remote", $"Temporarily staged PSWindowsUpdate 2.2.1.5 on {computerName}.");
            return new RemoteStageResult(target, true, token);
        }
        catch
        {
            TryRemoveOwnedPath(target, token);
            throw;
        }
    }

    public bool TryRemoveOwned(string computerName, string token)
    {
        ValidateComputerName(computerName);
        return TryRemoveOwnedPath(GetTargetPath(computerName), token);
    }

    public static void ValidateComputerName(string computerName)
    {
        if (string.IsNullOrWhiteSpace(computerName) || computerName.Length > 255 ||
            Uri.CheckHostName(computerName) == UriHostNameType.Unknown)
        {
            throw new ArgumentException("Enter a valid DNS host name for the remote computer.", nameof(computerName));
        }
    }

    private bool TryRemoveOwnedPath(string target, string token)
    {
        try
        {
            var marker = Path.Combine(target, OwnerMarker);
            if (!File.Exists(marker) || !string.Equals(File.ReadAllText(marker).Trim(), token, StringComparison.Ordinal))
            {
                return false;
            }

            var canonical = Path.GetFullPath(target);
            if (!canonical.EndsWith(Path.Combine("PSWindowsUpdate", "2.2.1.5"), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Directory.Delete(target, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetTargetPath(string computerName) =>
        $@"\\{computerName}\C$\Program Files\WindowsPowerShell\Modules\PSWindowsUpdate\2.2.1.5";

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("X2")));
    }
}

internal sealed class RemoteStageResult
{
    public RemoteStageResult(string path, bool wasCreated, string ownershipToken)
    {
        Path = path;
        WasCreated = wasCreated;
        OwnershipToken = ownershipToken;
    }

    public string Path { get; }

    public bool WasCreated { get; }

    public string OwnershipToken { get; }
}
