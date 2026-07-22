using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class OperationCatalogTests
{
    [TestMethod]
    public void EveryLegacyCommandHasAnExplicitReplacement()
    {
        Assert.AreEqual(19, OperationCatalog.LegacyMappings.Count);
        Assert.AreEqual(19, OperationCatalog.LegacyMappings.Select(item => item.LegacyCommand).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.IsTrue(OperationCatalog.LegacyMappings.All(item => !string.IsNullOrWhiteSpace(item.Replacement)));
    }

    [TestMethod]
    public void PublicOperationNamesAreUniqueAndCoverCoreWorkflows()
    {
        Assert.AreEqual(OperationCatalog.Operations.Count, OperationCatalog.Operations.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var names = OperationCatalog.Operations.Select(item => item.Name).ToArray();
        CollectionAssert.Contains(names, "scan");
        CollectionAssert.Contains(names, "install");
        CollectionAssert.Contains(names, "offline-scan");
        CollectionAssert.Contains(names, "policy");
        CollectionAssert.Contains(names, "job");
        CollectionAssert.Contains(names, "report");
    }
}
