using WarpCLR.IR;
using WarpCLR.Runtime.Device;

namespace WarpCLR.CSharp.Tests;

[TestClass]
public sealed class ScopedMemoryFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Using_scope_reclaims_unsigned_arrays(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        using WarpScope scope = WarpCLRMemory.Scope(128);
        WarpScopedUInt32Array values = scope.AllocateUInt32Array(4);

        values[0] = uint.MaxValue;
        values[1] = 0x80000000u;

        Assert.AreEqual(4, values.Length);
        Assert.AreEqual(uint.MaxValue, values[0]);
        Assert.AreEqual(0x80000000u, values[1]);
        Assert.AreEqual(1, scope.AllocationCount);
    }

    [TestMethod]
    [FourBackends]
    public void Scoped_objects_preserve_reference_fields(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        WarpTypeLayout layout = CreateNodeLayout();
        using WarpScope scope = WarpCLRMemory.Scope(128);
        WarpScopedObject parent = scope.AllocateObject(layout);
        WarpScopedObject child = scope.AllocateObject(layout);

        parent.WriteUInt32("value", 0xDEADBEEFu);
        parent.WriteObject("next", child);

        Assert.AreEqual(0xDEADBEEFu, parent.ReadUInt32("value"));
        Assert.IsTrue(parent.TryReadObject("next", out WarpScopedObject actual));
        actual.WriteUInt32("value", 17u);
        Assert.AreEqual(17u, child.ReadUInt32("value"));
    }

    [TestMethod]
    [FourBackends]
    public void Scoped_reference_cannot_cross_activations(WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
        WarpTypeLayout layout = CreateNodeLayout();
        using WarpScope first = WarpCLRMemory.Scope(64);
        using WarpScope second = WarpCLRMemory.Scope(64);
        WarpScopedObject parent = first.AllocateObject(layout);
        WarpScopedObject child = second.AllocateObject(layout);

        WarpDeviceRuntimeException exception;
        try
        {
            parent.WriteObject("next", child);
            Assert.Fail("The cross-activation write did not fail.");
            return;
        }
        catch (WarpDeviceRuntimeException caught)
        {
            exception = caught;
        }

        Assert.AreEqual("WRPDEV1004", exception.Code);
    }

    private static WarpTypeLayout CreateNodeLayout() => new(
        "WarpCLR.CSharp.Tests.Node",
        size: 8,
        alignment: 8,
        fields:
        [
            new WarpFieldLayout("value", WarpFieldKind.UInt32, 0),
            new WarpFieldLayout("next", WarpFieldKind.ManagedReference, 4),
        ]);
}
