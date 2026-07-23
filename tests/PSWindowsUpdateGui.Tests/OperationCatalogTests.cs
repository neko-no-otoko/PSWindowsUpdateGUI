using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class OperationCatalogTests
{
    [TestMethod]
    public void PublicOperationCatalogIsCompleteAndWellFormed()
    {
        var names = OperationCatalog.Operations.Select(item => item.Name).ToArray();
        var expected = new[]
        {
            "scan", "download", "install", "uninstall", "hide", "unhide",
            "history", "status", "services", "offline-scan", "export-payload",
            "policy", "maintenance", "job", "report"
        };

        CollectionAssert.AreEquivalent(expected, names);
        Assert.AreEqual(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.IsTrue(OperationCatalog.Operations.All(item =>
            !string.IsNullOrWhiteSpace(item.Category) &&
            !string.IsNullOrWhiteSpace(item.Description)));
    }
}
