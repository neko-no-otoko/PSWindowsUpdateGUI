using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PSWindowsUpdateGui.Services;

internal sealed class OfflineCatalogDownload
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

internal sealed class OfflineCatalogService
{
    private static readonly Uri CatalogUri = new Uri("https://go.microsoft.com/fwlink/?LinkID=74689");

    public async Task<OfflineCatalogDownload> DownloadAsync(string destination, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(path), ".cab", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("The offline scan catalog destination must end in .cab.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await client.GetAsync(CatalogUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("The offline catalog download redirected away from HTTPS.");
            using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(target, 81920, cancellationToken).ConfigureAwait(false);

            var info = new FileInfo(temporary);
            if (info.Length < 1024 * 1024) throw new InvalidDataException("The downloaded offline scan catalog is unexpectedly small.");
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            return new OfflineCatalogDownload { Path = path, SizeBytes = new FileInfo(path).Length, Sha256 = Hash(path) };
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }
}
