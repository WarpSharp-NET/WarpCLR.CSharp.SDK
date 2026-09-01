using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.CSharp;

public sealed class WarpCLRSession
{
    private readonly WarpLoadedModule module;
    private readonly WarpDevelopmentSession session;

    internal WarpCLRSession(
        WarpLoadedModule module,
        WarpBackendKind backend)
    {
        this.module = module;
        session = new WarpDevelopmentSession(
            module,
            backend,
            WarpDevelopmentExecutionMode.SemanticEmulation);
    }

    public WarpBackendKind Backend => session.Backend;

    public WarpDevelopmentExecutionMode Mode => session.Mode;

    public WarpUInt32Buffer Dispatch(
        WarpMapEntry entry,
        IReadOnlyList<WarpUInt32Buffer> inputs,
        IReadOnlyList<uint>? scalarArguments = null)
    {
        ValidateDescriptor(
            entry.Identity,
            entry.InputBufferCount,
            entry.ScalarArgumentCount);

        uint[] output = session.DispatchIntegerMap(
            entry.Identity,
            GetInputStorage(inputs),
            scalarArguments);
        return new WarpUInt32Buffer(output, takeOwnership: true);
    }

    public uint Reduce(
        WarpReductionEntry entry,
        IReadOnlyList<WarpUInt32Buffer> inputs,
        IReadOnlyList<uint>? scalarArguments = null)
    {
        ValidateDescriptor(
            entry.Identity,
            entry.InputBufferCount,
            entry.ScalarArgumentCount);

        return session.DispatchUInt32Reduction(
            entry.Identity,
            GetInputStorage(inputs),
            scalarArguments);
    }

    private void ValidateDescriptor(
        string identity,
        int inputBufferCount,
        int scalarArgumentCount)
    {
        if (!module.Entries.TryGetValue(identity, out WarpLoadedEntry? loadedEntry))
        {
            throw new WarpHostException(
                "WRPHOST1001",
                $"Entry point '{identity}' is not loaded.");
        }

        if (loadedEntry.InputBufferCount != inputBufferCount ||
            loadedEntry.ScalarArgumentCount != scalarArgumentCount)
        {
            throw new WarpHostException(
                "WRPHOST1004",
                $"Entry descriptor '{identity}' does not match the loaded entry point.");
        }
    }

    private static IReadOnlyList<uint[]> GetInputStorage(
        IReadOnlyList<WarpUInt32Buffer> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var storage = new uint[inputs.Count][];
        for (int index = 0; index < inputs.Count; index++)
        {
            WarpUInt32Buffer input = inputs[index]
                ?? throw new ArgumentException(
                    "An input buffer cannot be null.",
                    nameof(inputs));
            storage[index] = input.GetStorage();
        }

        return storage;
    }
}
