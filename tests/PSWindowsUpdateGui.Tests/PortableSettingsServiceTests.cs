using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class PortableSettingsServiceTests
{
    [TestMethod]
    public void DataDirectoryIsAdjacentToExecutableDirectory()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "PSWindowsUpdateGUI.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(testRoot);
            var service = new PortableSettingsService(testRoot);

            Assert.IsFalse(service.IsEphemeral);
            Assert.AreEqual(Path.Combine(testRoot, "PSWindowsUpdateGUI.Data"), service.DataDirectory);
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }
    }
}
