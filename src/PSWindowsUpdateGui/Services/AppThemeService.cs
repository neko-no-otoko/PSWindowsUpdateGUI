using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace PSWindowsUpdateGui.Services;

internal sealed class AppThemeService : IDisposable
{
    internal const string SystemPreference = "System";
    internal const string LightPreference = "Light";
    internal const string DarkPreference = "Dark";

    public event EventHandler? ThemeChanged;

    public string Preference { get; private set; } = SystemPreference;

    public ElementTheme ElementTheme => Preference switch
    {
        LightPreference => ElementTheme.Light,
        DarkPreference => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    public TitleBarTheme TitleBarTheme => Preference switch
    {
        LightPreference => TitleBarTheme.Light,
        DarkPreference => TitleBarTheme.Dark,
        _ => TitleBarTheme.UseDefaultAppMode
    };

    public static string NormalizePreference(string? preference)
    {
        if (string.Equals(preference, LightPreference, StringComparison.OrdinalIgnoreCase)) return LightPreference;
        if (string.Equals(preference, DarkPreference, StringComparison.OrdinalIgnoreCase)) return DarkPreference;
        return SystemPreference;
    }

    public void Apply(string? preference)
    {
        Preference = NormalizePreference(preference);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() { }
}
