using WarpCLR.IR;

namespace Warp.CSharp.Tests;

[TestClass]
public sealed class EntryContractTests
{
    [TestMethod]
    [FourBackends]
    public void Map_descriptor_preserves_the_complete_contract(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        var entry = new WarpMapEntry("Example.Kernels.Transform", 2, 1);

        Assert.AreEqual("Example.Kernels.Transform", entry.Identity);
        Assert.AreEqual(2, entry.InputBufferCount);
        Assert.AreEqual(1, entry.ScalarArgumentCount);
    }

    [TestMethod]
    [FourBackends]
    public void Reduction_descriptor_rejects_map_execution(WarpBackendKind backend)
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
    public void Entry_descriptor_requires_an_input_buffer(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new WarpMapEntry("Example.Kernels.Transform", 0, 1));
    }
}
