using System;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class CommandCatalogTests
{
    [TestMethod]
    public void RegistryContainsEveryManifestExportedCommand()
    {
        Assert.AreEqual(19, CommandRegistry.PublicCommands.Length);
        Assert.AreEqual(19, CommandRegistry.PublicCommands.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        CollectionAssert.Contains(CommandRegistry.PublicCommands, "Get-WindowsUpdate");
        CollectionAssert.Contains(CommandRegistry.PublicCommands, "Set-WUSettings");
        CollectionAssert.Contains(CommandRegistry.PublicCommands, "Get-WUOfflineMSU");
    }

    [TestMethod]
    public async Task RuntimeCatalogCoversEveryAllowlistedCommandAndParameterSet()
    {
        var settings = new PortableSettings();
        var data = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PSWUGuiTests", Guid.NewGuid().ToString("N"));
        var log = new PortableLogService(data, settings);
        using var module = ModuleRuntime.Create();
        using var host = new PowerShellHost(module, log);
        await host.InitializeAsync();
        var catalog = await host.LoadCatalogAsync();

        CollectionAssert.AreEquivalent(CommandRegistry.PublicCommands, catalog.Select(command => command.Name).ToArray());
        Assert.IsTrue(catalog.All(command => command.ParameterSets.Count > 0));
        var update = catalog.Single(command => command.Name == "Get-WindowsUpdate");
        Assert.IsTrue(update.ParameterSets.SelectMany(set => set.Parameters).Any(parameter => parameter.Name == "Install"));
        Assert.IsTrue(update.ParameterSets.SelectMany(set => set.Parameters).Any(parameter => parameter.Name == "ComputerName"));
    }
}
