using System;
using System.IO;
using System.Runtime.Serialization.Json;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Services;

internal sealed class PortableSettingsService
{
    private readonly string _settingsPath;

    public PortableSettingsService()
    {
        DataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PSWindowsUpdateGUI.Data");
        _settingsPath = Path.Combine(DataDirectory, "settings.json");
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var probe = Path.Combine(DataDirectory, $".write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
        }
        catch
        {
            IsEphemeral = true;
            DataDirectory = Path.Combine(Path.GetTempPath(), "PSWindowsUpdateGUI", "ephemeral", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            _settingsPath = Path.Combine(DataDirectory, "settings.json");
        }
    }

    public string DataDirectory { get; private set; }

    public bool IsEphemeral { get; }

    public PortableSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new PortableSettings();
        }

        try
        {
            using var stream = File.OpenRead(_settingsPath);
            var serializer = new DataContractJsonSerializer(typeof(PortableSettings));
            return (PortableSettings)(serializer.ReadObject(stream) ?? new PortableSettings());
        }
        catch
        {
            return new PortableSettings();
        }
    }

    public bool Save(PortableSettings settings)
    {
        if (IsEphemeral)
        {
            return false;
        }

        var temporary = _settingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = File.Create(temporary))
            {
                var serializer = new DataContractJsonSerializer(typeof(PortableSettings));
                serializer.WriteObject(stream, settings);
            }

            if (File.Exists(_settingsPath))
            {
                try { File.Replace(temporary, _settingsPath, null); }
                catch (IOException) { File.Copy(temporary, _settingsPath, true); }
            }
            else File.Move(temporary, _settingsPath);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
