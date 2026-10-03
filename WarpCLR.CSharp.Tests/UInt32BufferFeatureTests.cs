using WarpCLR.IR;

namespace WarpCLR.CSharp.Tests;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class UInt32BufferFeatureTests
{
    private static readonly uint[] ExactUnsignedValues = [0u, 0xDEADBEEFu, 0x80000000u, uint.MaxValue];
    [TestMethod]
    [FourBackends]
    public void BufferHasExactUnsignedValues(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        WarpUInt32Buffer buffer = WarpUInt32Buffer.From(
            0u,
            1u,
            0x80000000u,
            uint.MaxValue);

        buffer[1] = 0xDEADBEEFu;

        CollectionAssert.AreEqual(
            ExactUnsignedValues,
            buffer.ToArray());
    }

    [TestMethod]
    [FourBackends]
    public void BufferCopiesSourceStorage(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        uint[] source = [1u, 2u, 3u];
        WarpUInt32Buffer buffer = WarpUInt32Buffer.From(source);

        source[0] = uint.MaxValue;

        Assert.AreEqual(1u, buffer[0]);
    }

    [TestMethod]
    [FourBackends]
    public void ValueEnumerationPreservesReferenceEnumerationContract(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        WarpUInt32Buffer buffer = WarpUInt32Buffer.From(ExactUnsignedValues);
        var actual = new List<uint>();
        foreach (uint value in buffer.EnumerateValues())
        {
            actual.Add(value);
        }

        CollectionAssert.AreEqual(ExactUnsignedValues, actual);
        using IEnumerator<uint> reference = buffer.GetEnumerator();
        foreach (ref readonly uint value in ExactUnsignedValues.AsSpan())
        {
            Assert.IsTrue(reference.MoveNext());
            Assert.AreEqual(value, reference.Current);
        }

        Assert.IsFalse(reference.MoveNext());
        Assert.IsFalse(reference.MoveNext());
    }

    [TestMethod]
    [FourBackends]
    public void ValueEnumeratorRejectsUnpositionedCurrentAndHandlesDefault(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        WarpUInt32BufferEnumerator empty = default;
        Assert.IsFalse(empty.MoveNext());
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = empty.Current);
        WarpUInt32BufferEnumerator enumerator = WarpUInt32Buffer.From(1u).EnumerateValues();
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.IsTrue(enumerator.MoveNext());
        Assert.AreEqual(1u, enumerator.Current);
        Assert.IsFalse(enumerator.MoveNext());
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
    }
}
