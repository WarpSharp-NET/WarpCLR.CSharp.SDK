using WarpCLR.Runtime.Device;

namespace WarpCLR.CSharp;

public readonly ref struct WarpScopedUInt32Array
{
    private readonly WarpScopedRegion? region;
    private readonly WarpManagedReference reference;

    internal WarpScopedUInt32Array(
        WarpScopedRegion region,
        WarpManagedReference reference)
    {
        this.region = region;
        this.reference = reference;
    }

    public int Length => GetRegion().GetArrayLength(reference);

    public uint this[int index]
    {
        get => GetRegion().ReadUInt32ArrayElement(reference, index);
        set => GetRegion().WriteUInt32ArrayElement(reference, index, value);
    }

    private WarpScopedRegion GetRegion() => region
        ?? throw new WarpDeviceRuntimeException(
            "WRPDEV1001",
            "A scoped reference is null.");
}
