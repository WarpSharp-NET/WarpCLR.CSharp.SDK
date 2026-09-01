using WarpCLR.Runtime.Device;

namespace WarpCLR.CSharp;

public readonly ref struct WarpScopedObject
{
    private readonly WarpScopedRegion? region;
    private readonly WarpManagedReference reference;

    internal WarpScopedObject(
        WarpScopedRegion region,
        WarpManagedReference reference)
    {
        this.region = region;
        this.reference = reference;
    }

    public bool IsNull => region is null || reference.IsNull;

    public uint ReadUInt32(string fieldName) =>
        GetRegion().ReadUInt32Field(reference, fieldName);

    public void WriteUInt32(string fieldName, uint value) =>
        GetRegion().WriteUInt32Field(reference, fieldName, value);

    public bool TryReadObject(
        string fieldName,
        out WarpScopedObject value)
    {
        WarpScopedRegion activeRegion = GetRegion();
        WarpManagedReference result = activeRegion.ReadReferenceField(reference, fieldName);
        if (result.IsNull)
        {
            value = default;
            return false;
        }

        value = new WarpScopedObject(activeRegion, result);
        return true;
    }

    public void WriteObject(string fieldName, WarpScopedObject value)
    {
        WarpScopedRegion activeRegion = GetRegion();
        WarpScopedRegion valueRegion = value.GetRegion();
        if (!ReferenceEquals(activeRegion, valueRegion))
        {
            throw new WarpDeviceRuntimeException(
                "WRPDEV1004",
                "A scoped reference is outside its activation lifetime.");
        }

        activeRegion.WriteReferenceField(reference, fieldName, value.reference);
    }

    public void ClearObject(string fieldName) =>
        GetRegion().WriteReferenceField(
            reference,
            fieldName,
            WarpManagedReference.Null);

    private WarpScopedRegion GetRegion() => region
        ?? throw new WarpDeviceRuntimeException(
            "WRPDEV1001",
            "A scoped reference is null.");
}
