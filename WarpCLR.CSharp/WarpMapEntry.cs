namespace WarpCLR.CSharp;

public readonly record struct WarpMapEntry
{
    public WarpMapEntry(
        string identity,
        int inputBufferCount,
        int scalarArgumentCount)
    {
        WarpEntryContract.Validate(identity, inputBufferCount, scalarArgumentCount);
        Identity = identity;
        InputBufferCount = inputBufferCount;
        ScalarArgumentCount = scalarArgumentCount;
    }

    public string Identity { get; }

    public int InputBufferCount { get; }

    public int ScalarArgumentCount { get; }
}
