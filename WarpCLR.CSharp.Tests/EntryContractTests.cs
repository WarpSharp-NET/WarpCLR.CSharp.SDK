using WarpCLR.IR;

namespace WarpCLR.CSharp.Tests;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class EntryContractTests
{
    [TestMethod]
    [FourBackends]
    public void MapDescriptorPreservesTheCompleteContract(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        var entry = new WarpMapEntry("Example.Kernels.Transform", 2, 1);

        Assert.AreEqual("Example.Kernels.Transform", entry.Identity, StringComparer.Ordinal);
        Assert.AreEqual(2, entry.InputBufferCount);
        Assert.AreEqual(1, entry.ScalarArgumentCount);
    }

    [TestMethod]
    [FourBackends]
    public void ReductionDescriptorRejectsMapExecution(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new WarpReductionEntry(
                "Example.Kernels.Reduce",
                1,
                0,
                WarpExecution.Map));
    }

    [TestMethod]
    [FourBackends]
    public void EntryDescriptorRequiresAnInputBuffer(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new WarpMapEntry("Example.Kernels.Transform", 0, 1));
    }
}
