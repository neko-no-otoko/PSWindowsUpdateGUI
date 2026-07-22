using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class UpdateRowTests
{
    [TestMethod]
    public void MapsNestedWindowsUpdateIdentity()
    {
        var value = new PSObject();
        value.Properties.Add(new PSNoteProperty("Title", "A driver"));
        value.Properties.Add(new PSNoteProperty("Identity", new UpdateIdentity { UpdateID = "update-123" }));

        var row = UpdateRow.From(value);

        Assert.AreEqual("A driver", row.Title);
        Assert.AreEqual("update-123", row.UpdateId);
    }

    private sealed class UpdateIdentity
    {
        public string UpdateID { get; set; } = string.Empty;
    }
}
