using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace PSWindowsUpdateGui.Services;

internal sealed class ModuleRuntime : IDisposable
{
    private const string PackageResource = "PSWindowsUpdateGui.Resources.PSWindowsUpdate.nupkg";
    private const string ManifestResource = "PSWindowsUpdateGui.Resources.vendor-manifest.json";
    private readonly string _runtimeRoot;
    private bool _disposed;

    private ModuleRuntime(string runtimeRoot, VendorManifest manifest)
    {
        _runtimeRoot = runtimeRoot;
        Manifest = manifest;
    }

    public VendorManifest Manifest { get; }

    public string ModuleDirectory => Path.Combine(_runtimeRoot, "module");

    public string ModuleDllPath => Path.Combine(ModuleDirectory, "PSWindowsUpdate.dll");

    public static ModuleRuntime Create()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var manifest = ReadManifest(assembly);
        var parent = Path.Combine(Path.GetTempPath(), "PSWindowsUpdateGUI");
        CleanupStaleDirectories(parent);

        var runtimeRoot = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtimeRoot);
        ApplyRestrictedAcl(runtimeRoot);

        var packagePath = Path.Combine(runtimeRoot, "PSWindowsUpdate.nupkg");
        using (var input = assembly.GetManifestResourceStream(PackageResource) ??
                           throw new InvalidOperationException("The embedded PSWindowsUpdate package is missing."))
        using (var output = File.Create(packagePath))
        {
            input.CopyTo(output);
        }

        EnsureHash(packagePath, manifest.PackageSha256);
        var moduleDirectory = Path.Combine(runtimeRoot, "module");
        Directory.CreateDirectory(moduleDirectory);
        ExtractSafely(packagePath, moduleDirectory);

        foreach (var file in manifest.Files)
        {
            EnsureHash(Path.Combine(moduleDirectory, file.Path), file.Sha256);
        }

        VerifyAuthenticodeSignatures(moduleDirectory);
        File.Delete(packagePath);
        return new ModuleRuntime(runtimeRoot, manifest);
    }

    private static VendorManifest ReadManifest(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(ManifestResource) ??
                           throw new InvalidOperationException("The embedded vendor manifest is missing.");
        var serializer = new DataContractJsonSerializer(typeof(VendorManifest));
        return (VendorManifest)(serializer.ReadObject(stream) ?? throw new InvalidDataException("Vendor manifest is empty."));
    }

    private static void ExtractSafely(string packagePath, string destinationRoot)
    {
        var canonicalRoot = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, relative));
            if (!destination.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe path in embedded package: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? destinationRoot);
            using var input = entry.Open();
            using var output = File.Create(destination);
            input.CopyTo(output);
        }
    }

    private static void EnsureHash(string path, string expected)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Required module file is missing: {Path.GetFileName(path)}");
        }

        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var actual = string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("X2")));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Integrity check failed for {Path.GetFileName(path)}.");
        }
    }

    private static void VerifyAuthenticodeSignatures(string moduleDirectory)
    {
        var signedFiles = new[]
        {
            "PSWindowsUpdate.dll",
            "PSWindowsUpdate.psd1",
            "PSWindowsUpdate.psm1",
            "PSWindowsUpdate.Format.ps1xml"
        };

        foreach (var file in signedFiles)
        {
            using var powerShell = PowerShell.Create();
            powerShell.AddCommand("Get-AuthenticodeSignature")
                .AddParameter("FilePath", Path.Combine(moduleDirectory, file));
            var signature = powerShell.Invoke().FirstOrDefault();
            var status = signature?.Properties["Status"]?.Value?.ToString();
            if (!string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Authenticode verification failed for {file}: {status ?? "unknown"}.");
            }
        }
    }

    private static void ApplyRestrictedAcl(string path)
    {
        var current = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current user SID is unavailable.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(current, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void CleanupStaleDirectories(string parent)
    {
        if (!Directory.Exists(parent))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(parent))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(directory) < DateTime.UtcNow.AddHours(-1))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch
            {
                // A previous process can still have the module DLL loaded. It will be retried next launch.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Directory.Delete(_runtimeRoot, true);
        }
        catch
        {
            // .NET Framework keeps binary modules loaded for the process lifetime.
        }
    }
}

[DataContract]
internal sealed class VendorManifest
{
    [DataMember(Name = "packageVersion", Order = 1)]
    public string PackageVersion { get; set; } = string.Empty;

    [DataMember(Name = "packageSha256", Order = 2)]
    public string PackageSha256 { get; set; } = string.Empty;

    [DataMember(Name = "files", Order = 3)]
    public List<VendorFile> Files { get; set; } = new List<VendorFile>();
}

[DataContract]
internal sealed class VendorFile
{
    [DataMember(Name = "path", Order = 1)]
    public string Path { get; set; } = string.Empty;

    [DataMember(Name = "sha256", Order = 2)]
    public string Sha256 { get; set; } = string.Empty;
}
