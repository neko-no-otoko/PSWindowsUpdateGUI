using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSWindowsUpdateGui.Services;

namespace PSWindowsUpdateGui.Tests;

[TestClass]
public sealed class StaComWorkerTests
{
    [TestMethod]
    public async Task WorkerOwnsADedicatedStaApartment()
    {
        using var worker = new StaComWorker("test");
        var state = await worker.InvokeAsync(() => Thread.CurrentThread.GetApartmentState());
        Assert.AreEqual(ApartmentState.STA, state);
    }
}
