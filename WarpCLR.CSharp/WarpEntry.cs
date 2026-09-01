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

public readonly record struct WarpReductionEntry
{
    public WarpReductionEntry(
        string identity,
        int inputBufferCount,
        int scalarArgumentCount,
        WarpExecution execution)
    {
        WarpEntryContract.Validate(identity, inputBufferCount, scalarArgumentCount);
        if (execution == WarpExecution.Map || !Enum.IsDefined(execution))
        {
            throw new ArgumentOutOfRangeException(
                nameof(execution),
                execution,
                "A reduction entry requires a registered reduction mode.");
        }

        Identity = identity;
        InputBufferCount = inputBufferCount;
        ScalarArgumentCount = scalarArgumentCount;
        Execution = execution;
    }

    public string Identity { get; }

    public int InputBufferCount { get; }

    public int ScalarArgumentCount { get; }

    public WarpExecution Execution { get; }
}

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
