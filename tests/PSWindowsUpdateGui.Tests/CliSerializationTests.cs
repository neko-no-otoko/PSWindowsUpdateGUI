using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Cli;
using PSWindowsUpdateGui.Models;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class CliSerializationTests
{
    [TestMethod]
    public void MaterializedHistoryListSerializesInEnvelope()
    {
        var history = new List<HistoryRecord>
        {
            new HistoryRecord
            {
                DateUtc = new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc),
                Title = "Test update",
                Operation = "uoInstallation",
                Result = "orcSucceeded"
            }
        };
        var envelope = new OperationEnvelope<List<HistoryRecord>> { Data = history };

        var json = CliApplication.SerializeJson(envelope);

        StringAssert.Contains(json, "Test update");
        StringAssert.Contains(json, "\"data\"");
    }

    [TestMethod]
    public void InvalidWuaCriteriaIsAValidationFailure()
    {
        var exception = new COMException("Invalid criteria", unchecked((int)0x80240032));

        Assert.IsTrue(CliApplication.IsValidationFailure(exception));
    }
}
