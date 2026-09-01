namespace Warp.CSharp;

public static class Warp
{
    public static WarpScope Scope(int capacityBytes) => new(capacityBytes);
}
