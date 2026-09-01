namespace WarpCLR.CSharp;

public static class WarpCLRMemory
{
    public static WarpScope Scope(int capacityBytes) => new(capacityBytes);
}
