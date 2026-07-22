using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class HostSecurityTests
{
    [TestMethod]
    public void ExtractedModuleUsesRestrictedAclAndCleansUp()
    {
        string runtimeRoot;
        using (var module = ModuleRuntime.Create())
        {
            runtimeRoot = Directory.GetParent(module.ModuleDirectory)!.FullName;
            Assert.IsTrue(File.Exists(module.ModuleDllPath));
            var rules = new DirectoryInfo(runtimeRoot).GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToArray();
            var world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            Assert.IsFalse(rules.Any(rule => world.Equals(rule.IdentityReference) && rule.AccessControlType == AccessControlType.Allow));
        }

        Assert.IsFalse(Directory.Exists(runtimeRoot));
    }

    [TestMethod]
    public void PreviewQuotesUntrustedTextAndRedactsCredentials()
    {
        var data = Path.Combine(Path.GetTempPath(), "PSWUGuiTests", Guid.NewGuid().ToString("N"));
        using var module = ModuleRuntime.Create();
        using var host = new PowerShellHost(module, new PortableLogService(data, new PortableSettings()));
        var preview = host.RenderPreview("Get-WindowsUpdate", new Dictionary<string, object?>
        {
            ["Title"] = "'; Remove-Item C:\\Windows; '"
        });

        StringAssert.StartsWith(preview, "Get-WindowsUpdate -Title '");
        StringAssert.Contains(preview, "''; Remove-Item");
    }

    [TestMethod]
    public async Task HostRejectsCommandsOutsideTheCatalog()
    {
        var data = Path.Combine(Path.GetTempPath(), "PSWUGuiTests", Guid.NewGuid().ToString("N"));
        using var module = ModuleRuntime.Create();
        using var host = new PowerShellHost(module, new PortableLogService(data, new PortableSettings()));

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            host.InvokeAsync("Invoke-Expression", new Dictionary<string, object?>(), CancellationToken.None));
    }

    [TestMethod]
    public void RemotePreflightFailsWhenARequiredCheckFails()
    {
        var result = new RemotePreflightResult("host.contoso.test", false);
        for (var index = 0; index < 12; index++)
        {
            result.Add("Check " + index, index != 4, "test");
        }

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public void HostExpandsCollectionOutputIntoIndividualPipelineRecords()
    {
        var collection = new ArrayList { "first", "second" };
        var expanded = PowerShellHost.ExpandOutput(PSObject.AsPSObject(collection)).ToArray();

        Assert.AreEqual(2, expanded.Length);
        Assert.AreEqual("first", expanded[0].BaseObject);
        Assert.AreEqual("second", expanded[1].BaseObject);
        Assert.AreEqual(1, PowerShellHost.ExpandOutput(PSObject.AsPSObject("text")).Count());
    }
}
