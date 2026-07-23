using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class AppThemeServiceTests
{
    [DataTestMethod]
    [DataRow(null, "System")]
    [DataRow("", "System")]
    [DataRow("unknown", "System")]
    [DataRow("system", "System")]
    [DataRow("LIGHT", "Light")]
    [DataRow("dark", "Dark")]
    public void ThemePreferenceIsNormalized(string? input, string expected)
    {
        Assert.AreEqual(expected, AppThemeService.NormalizePreference(input));
    }
}
