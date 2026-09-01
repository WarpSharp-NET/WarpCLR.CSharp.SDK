using WarpCLR.IR;

namespace WarpCLR.CSharp.Tests;

[TestClass]
public sealed class UInt32BufferFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Buffer_has_exact_unsigned_values(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        WarpUInt32Buffer buffer = WarpUInt32Buffer.From(
            0u,
            1u,
            0x80000000u,
            uint.MaxValue);

        buffer[1] = 0xDEADBEEFu;

        CollectionAssert.AreEqual(
            new uint[] { 0u, 0xDEADBEEFu, 0x80000000u, uint.MaxValue },
            buffer.ToArray());
    }

    [TestMethod]
    [FourBackends]
    public void Buffer_copies_source_storage(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        uint[] source = [1u, 2u, 3u];
        WarpUInt32Buffer buffer = WarpUInt32Buffer.From(source);

        source[0] = uint.MaxValue;

        Assert.AreEqual(1u, buffer[0]);
    }
}
