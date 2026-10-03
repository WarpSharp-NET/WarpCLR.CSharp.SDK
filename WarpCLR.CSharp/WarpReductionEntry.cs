namespace WarpCLR.CSharp;

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
