using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class PlatformTests
{
    [TestMethod]
    public void WindowsBuildComesFromAuthoritativeCurrentVersionData()
    {
        Assert.IsTrue(App.GetWindowsBuildNumber() >= 22000);
    }
}
