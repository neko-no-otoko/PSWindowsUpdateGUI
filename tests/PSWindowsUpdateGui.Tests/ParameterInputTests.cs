using System;
using System.Management.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Models;
using PSWindowsUpdateGui.Services;
using PSWindowsUpdateGui.ViewModels;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class ParameterInputTests
{
    [TestMethod]
    public void OptionalSwitchPreservesBoundFalse()
    {
        var input = new ParameterInputViewModel(new ParameterDefinition
        {
            Name = "Hide",
            ParameterType = typeof(SwitchParameter)
        })
        {
            IsBound = true,
            ValueText = "False"
        };

        var value = (SwitchParameter)input.ConvertValue()!;
        Assert.IsFalse(value.IsPresent);
    }

    [TestMethod]
    public void ArraysAreParsedWithoutExecutingInput()
    {
        var input = new ParameterInputViewModel(new ParameterDefinition
        {
            Name = "KBArticleID",
            ParameterType = typeof(string[])
        })
        {
            IsBound = true,
            ValueText = "KB5000001, KB5000002\r\nKB5000003"
        };

        CollectionAssert.AreEqual(new[] { "KB5000001", "KB5000002", "KB5000003" }, (string[])input.ConvertValue()!);
    }

    [TestMethod]
    public void ValidateRangeRejectsUnsafeValue()
    {
        var definition = new ParameterDefinition
        {
            Name = "ActiveHoursStart",
            ParameterType = typeof(int),
            Minimum = 0,
            Maximum = 23
        };
        var input = new ParameterInputViewModel(definition) { IsBound = true, ValueText = "24" };
        Assert.ThrowsException<FormatException>(() => input.ConvertValue());
    }

    [TestMethod]
    public void RemoteComputerMustBeDnsName()
    {
        RemoteModuleStager.ValidateComputerName("workstation-01.contoso.test");
        Assert.ThrowsException<ArgumentException>(() => RemoteModuleStager.ValidateComputerName("server\\C$\\Windows"));
    }

    [TestMethod]
    public void SecretsAreRedacted()
    {
        var redacted = PortableLogService.Redact("password=hunter2 token:abcd -Credential cleartext");
        Assert.IsFalse(redacted.Contains("hunter2"));
        Assert.IsFalse(redacted.Contains("abcd"));
        Assert.IsFalse(redacted.Contains("cleartext"));
    }
}
