using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;
using PSWindowsUpdateGui.ViewModels;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class HistoryItemViewModelTests
{
    private static readonly UpdateKey Key = UpdateKey.Parse("dac82a19-f9ba-459d-a30a-f56d4b326faa:3");

    [TestMethod]
    public void SuccessfulInstallationRequiresVerificationBeforeRemoval()
    {
        var item = new HistoryItemViewModel(new HistoryRecord
        {
            DateUtc = DateTime.UtcNow,
            Title = "Driver update",
            Operation = "uoInstallation",
            Result = "orcSucceeded",
            Identity = Key
        });

        Assert.IsTrue(item.CanCheckRemoval);
        Assert.IsFalse(item.CanUninstall);
        Assert.AreEqual("Installed", item.OperationDisplay);
        Assert.AreEqual("Succeeded", item.ResultDisplay);

        item.MarkRemovalVerified("Default");
        Assert.IsTrue(item.CanUninstall);
        Assert.AreEqual("Default", item.VerifiedSource);

        item.ResetRemovalVerification("Source changed.");
        Assert.IsFalse(item.CanUninstall);
        Assert.AreEqual("Source changed.", item.RemovalStatus);
    }

    [TestMethod]
    public void FailedOrUninstallHistoryEntriesCannotBeRemoved()
    {
        var failed = new HistoryItemViewModel(new HistoryRecord { Operation = "uoInstallation", Result = "orcFailed", Identity = Key });
        var uninstall = new HistoryItemViewModel(new HistoryRecord { Operation = "uoUninstallation", Result = "orcSucceeded", Identity = Key });

        Assert.IsFalse(failed.CanCheckRemoval);
        Assert.IsFalse(uninstall.CanCheckRemoval);
    }

    [TestMethod]
    public void RemoteRemovalPlanIsForwardedAsTypedArguments()
    {
        var arguments = RemoteGuiBridge.BuildExecuteArguments(new UpdateActionRequest
        {
            Action = UpdateActionKind.Uninstall,
            Source = UpdateSourceKind.MicrosoftUpdate,
            PlanOnly = true,
            Updates = { Key }
        }, "pc01.contoso.test", true);

        CollectionAssert.Contains(arguments, "--plan");
        CollectionAssert.Contains(arguments, "--use-ssl");
        CollectionAssert.Contains(arguments, Key.ToString());
        Assert.AreEqual(1, arguments.Count(value => value == "--update"));
    }
}
