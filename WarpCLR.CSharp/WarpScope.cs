using WarpCLR.Runtime.Device;

namespace WarpCLR.CSharp;

public ref struct WarpScope
{
    private readonly WarpScopedRegion? region;

    internal WarpScope(int capacityBytes)
    {
        region = new WarpScopedRegion(capacityBytes);
    }

    public int CapacityBytes => GetRegion().CapacityBytes;

    public int UsedBytes => GetRegion().UsedBytes;

    public int AllocationCount => GetRegion().AllocationCount;

    public bool IsDisposed => region is null || region.IsDisposed;

    public WarpScopedObject AllocateObject(WarpTypeLayout layout)
    {
        WarpScopedRegion activeRegion = GetRegion();
        return new WarpScopedObject(activeRegion, activeRegion.AllocateObject(layout));
    }

    public WarpScopedUInt32Array AllocateUInt32Array(int length)
    {
        WarpScopedRegion activeRegion = GetRegion();
        return new WarpScopedUInt32Array(
            activeRegion,
            activeRegion.AllocateUInt32Array(length));
    }

    public void Dispose()
    {
        GetRegion().Dispose();
    }

    private readonly WarpScopedRegion GetRegion() => region
        ?? throw new InvalidOperationException("The Warp scope was not initialized.");
}
