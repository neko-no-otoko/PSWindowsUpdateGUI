using System;
using System.Linq;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Cli;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class ValidationTests
{
    [TestMethod]
    public void UpdateIdentityRequiresGuidAndRevision()
    {
        var key = UpdateKey.Parse("dac82a19-f9ba-459d-a30a-f56d4b326faa:201");
        Assert.AreEqual("dac82a19-f9ba-459d-a30a-f56d4b326faa", key.UpdateId);
        Assert.AreEqual(201, key.Revision);
        Assert.ThrowsException<FormatException>(() => UpdateKey.Parse("KB5030000"));
        Assert.ThrowsException<FormatException>(() => UpdateKey.Parse("dac82a19-f9ba-459d-a30a-f56d4b326faa:-1"));
    }

    [TestMethod]
    public void CriteriaBuilderUsesTypedDefaults()
    {
        Assert.AreEqual("IsInstalled=0 and IsHidden=0 and Type='Driver'", WuaCriteria.Build(new ScanRequest { Type = UpdateKind.Driver }));
    }

    [TestMethod]
    public void CriteriaRejectsScriptAndUnsupportedProperties()
    {
        Assert.ThrowsException<FormatException>(() => WuaCriteria.Validate("IsInstalled=0; Remove-Item C:\\Windows"));
        Assert.ThrowsException<FormatException>(() => WuaCriteria.Validate("Title='x'"));
        WuaCriteria.Validate("IsInstalled=0 and Type='Software'");
    }

    [TestMethod]
    public void UndefinedNumericEnumsAreRejected()
    {
        Assert.ThrowsException<FormatException>(() => WuaCriteria.ValidateRequest(new ScanRequest { Source = (UpdateSourceKind)99 }));
        Assert.ThrowsException<FormatException>(() => WuaCriteria.ValidateRequest(new ScanRequest { Type = (UpdateKind)99 }));
    }

    [TestMethod]
    public void CliParserPreservesRepeatedTypedUpdateArguments()
    {
        var parsed = CliArguments.Parse(new[] { "install", "--update", "a", "--update", "b", "--plan" });
        CollectionAssert.AreEqual(new[] { "a", "b" }, parsed.GetAll("update").ToArray());
        Assert.IsTrue(parsed.Has("plan"));
    }

    [TestMethod]
    public void PolicyEditorRejectsUnknownAndOutOfRangeValues()
    {
        var service = new WindowsUpdatePolicyService();
        Assert.ThrowsException<FormatException>(() => service.Preview(new[] { "UnknownPolicy=1" }));
        Assert.ThrowsException<FormatException>(() => service.Preview(new[] { "ScheduledInstallTime=24" }));
        StringAssert.Contains(service.Preview(new[] { "ScheduledInstallTime=3" }), "ScheduledInstallTime = 3");
    }

    [TestMethod]
    public void RemoteTargetRequiresDnsName()
    {
        RemoteCliClient.ValidateComputerName("workstation-01.contoso.test");
        Assert.ThrowsException<FormatException>(() => RemoteCliClient.ValidateComputerName("server\\C$\\Windows"));
        Assert.ThrowsException<FormatException>(() => RemoteCliClient.ValidateComputerName("192.168.1.20"));
    }

    [TestMethod]
    public void LogRedactionCoversCredentialMaterial()
    {
        var redacted = PortableLogService.Redact("password=hunter2 token:abcd -Credential cleartext authorization=BearerSecret");
        Assert.IsFalse(redacted.Contains("hunter2"));
        Assert.IsFalse(redacted.Contains("abcd"));
        Assert.IsFalse(redacted.Contains("cleartext"));
    }

    [TestMethod]
    public void SystemWuaBinaryPassesAuthenticodeVerification()
    {
        AuthenticodeVerifier.VerifyOrThrow(Path.Combine(Environment.SystemDirectory, "wuapi.dll"));
    }

    [TestMethod]
    public void JsonEnvelopeRoundTripsTypedUpdateData()
    {
        var original = new OperationEnvelope<System.Collections.Generic.List<UpdateRecord>>
        {
            Status = OperationState.Success,
            CompletedUtc = DateTime.UtcNow,
            Data = new System.Collections.Generic.List<UpdateRecord>
            {
                new UpdateRecord { Identity = UpdateKey.Parse("dac82a19-f9ba-459d-a30a-f56d4b326faa:3"), Title = "Driver" }
            }
        };
        var serializer = new DataContractJsonSerializer(typeof(OperationEnvelope<System.Collections.Generic.List<UpdateRecord>>));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, original);
        stream.Position = 0;
        var roundTrip = (OperationEnvelope<System.Collections.Generic.List<UpdateRecord>>)serializer.ReadObject(stream)!;
        Assert.AreEqual(OperationState.Success, roundTrip.Status);
        Assert.AreEqual(original.Data[0].Identity, roundTrip.Data![0].Identity);
    }

    [TestMethod]
    public void RemoteProcessQuotingDoesNotChangeBackslashes()
    {
        Assert.AreEqual("\"\\d+\\s\"", RemoteGuiBridge.QuoteArgument(@"\d+\s"));
        Assert.AreEqual("\"C:\\Program Files\\App\\\\\"", RemoteGuiBridge.QuoteArgument(@"C:\Program Files\App\"));
        Assert.AreEqual("\"say \\\"hello\\\"\"", RemoteGuiBridge.QuoteArgument("say \"hello\""));
    }

    [TestMethod]
    public void ScheduledTaskXmlSeparatesExecutableAndValidatedManifestArguments()
    {
        var xml = ScheduledJobService.BuildTaskXml(@"C:\Program Files\PSWindowsUpdateGUI.exe", @"C:\ProgramData\PSWindowsUpdateGUI\Jobs\one.json", new DateTime(2026, 8, 1, 3, 0, 0));
        var document = XDocument.Parse(xml);
        var ns = document.Root!.Name.Namespace;
        Assert.AreEqual(@"C:\Program Files\PSWindowsUpdateGUI.exe", document.Descendants(ns + "Command").Single().Value);
        Assert.AreEqual("job run --manifest \"C:\\ProgramData\\PSWindowsUpdateGUI\\Jobs\\one.json\" --yes", document.Descendants(ns + "Arguments").Single().Value);
        Assert.AreEqual("2026-08-01T03:00:00", document.Descendants(ns + "StartBoundary").Single().Value);
    }
}
