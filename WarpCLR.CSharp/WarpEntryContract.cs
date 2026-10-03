namespace WarpCLR.CSharp;

internal static class WarpEntryContract
{
    public static void Validate(
        string identity,
        int inputBufferCount,
        int scalarArgumentCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputBufferCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(scalarArgumentCount);
    }
}
