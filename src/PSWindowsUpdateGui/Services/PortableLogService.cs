using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Services;

internal sealed class PortableLogService
{
    private static readonly Regex[] SecretPatterns =
    {
        new Regex("(?i)(password|secret|token)\\s*[:=]\\s*[^,;\\s]+", RegexOptions.Compiled),
        new Regex("(?i)(-Credential\\s+)\\S+", RegexOptions.Compiled)
    };

    private readonly object _sync = new object();
    private readonly string _logDirectory;
    private readonly PortableSettings _settings;

    public PortableLogService(string dataDirectory, PortableSettings settings)
    {
        _settings = settings;
        _logDirectory = Path.Combine(dataDirectory, "Logs");
        Directory.CreateDirectory(_logDirectory);
        Prune();
    }

    public event EventHandler<string>? EntryWritten;

    public string CurrentLogPath => Path.Combine(_logDirectory, $"PSWindowsUpdateGUI-{DateTime.UtcNow:yyyyMMdd}.log");

    public void Write(string category, string message)
    {
        var redacted = Redact(message).Replace("\r", " ").Replace("\n", " ");
        var entry = $"{DateTimeOffset.Now:O} [{category}] {redacted}";
        lock (_sync)
        {
            File.AppendAllText(CurrentLogPath, entry + Environment.NewLine);
        }

        EntryWritten?.Invoke(this, entry);
    }

    public string ReadCurrent()
    {
        lock (_sync)
        {
            return File.Exists(CurrentLogPath) ? File.ReadAllText(CurrentLogPath) : string.Empty;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            foreach (var file in Directory.GetFiles(_logDirectory, "*.log"))
            {
                File.Delete(file);
            }
        }
    }

    public static string Redact(string value)
    {
        var result = value;
        foreach (var pattern in SecretPatterns)
        {
            result = pattern.Replace(result, match => match.Groups[1].Value + "<redacted>");
        }

        return result;
    }

    private void Prune()
    {
        var files = Directory.GetFiles(_logDirectory, "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        foreach (var file in files.Where(file => file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-_settings.LogRetentionDays)))
        {
            file.Delete();
        }

        long remaining = Math.Max(1, _settings.LogSizeLimitMb) * 1024L * 1024L;
        foreach (var file in files.Where(file => file.Exists))
        {
            remaining -= file.Length;
            if (remaining < 0)
            {
                file.Delete();
            }
        }
    }
}
