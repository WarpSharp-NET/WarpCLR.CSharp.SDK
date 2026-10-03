using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.CSharp;

public sealed class WarpCLRRuntimeSession : IAsyncDisposable
{
    private readonly WarpRuntimeModule module;
    private readonly WarpRuntimeContext context;

    internal WarpCLRRuntimeSession(
        WarpRuntimeModule module,
        WarpBackendKind backend,
        WarpRuntimeOptions? options,
        WarpJitCache? jitCache)
    {
        this.module = module;
        context = new WarpRuntimeContext(module, backend, options, jitCache);
    }

    public WarpBackendKind Backend => context.Backend;

    public WarpRuntimeContextState State => context.State;

    public WarpJitCacheStatistics JitStatistics => context.JitStatistics;

    public async Task<WarpUInt32Buffer> DispatchAsync(
        WarpMapEntry entry,
        IReadOnlyList<WarpUInt32Buffer> inputs,
        IReadOnlyList<uint>? scalarArguments = null,
        CancellationToken cancellationToken = default)
    {
        WarpRuntimeEntry loadedEntry = ValidateDescriptor(entry.Identity, entry.InputBufferCount, entry.ScalarArgumentCount);
        if (loadedEntry.Reduction.HasValue)
        {
            throw new WarpHostException("WRPRUNTIME1004", "A map descriptor cannot dispatch a reduction entry.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        uint[][] storage = GetInputStorage(inputs, loadedEntry.InputBufferCount);
        uint[] output = await context.DispatchIntegerMapAsync(entry.Identity, storage, scalarArguments, cancellationToken)
            .ConfigureAwait(false);
        return new WarpUInt32Buffer(output, takeOwnership: true);
    }

    public Task<uint> ReduceAsync(
        WarpReductionEntry entry,
        IReadOnlyList<WarpUInt32Buffer> inputs,
        IReadOnlyList<uint>? scalarArguments = null,
        CancellationToken cancellationToken = default)
    {
        WarpRuntimeEntry loadedEntry = ValidateDescriptor(entry.Identity, entry.InputBufferCount, entry.ScalarArgumentCount);
        WarpReductionOperation? requested = entry.Execution switch
        {
            WarpExecution.ReduceWrappingSum => WarpReductionOperation.WrappingSum,
            WarpExecution.ReduceMinimum => WarpReductionOperation.Minimum,
            WarpExecution.ReduceMaximum => WarpReductionOperation.Maximum,
            _ => null,
        };
        if (!requested.HasValue || loadedEntry.Reduction != requested)
        {
            throw new WarpHostException("WRPRUNTIME1004", "The reduction descriptor does not match the verified entry's execution mode.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        uint[][] storage = GetInputStorage(inputs, loadedEntry.InputBufferCount);
        return context.DispatchUInt32ReductionAsync(entry.Identity, storage, scalarArguments, cancellationToken);
    }

    public ValueTask DisposeAsync() => context.DisposeAsync();

    private WarpRuntimeEntry ValidateDescriptor(string identity, int inputBufferCount, int scalarArgumentCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (!module.Entries.TryGetValue(identity, out WarpRuntimeEntry? entry) ||
            entry.InputBufferCount != inputBufferCount || entry.ScalarArgumentCount != scalarArgumentCount)
        {
            throw new WarpHostException("WRPRUNTIME1004", "The entry descriptor does not match an entry in the verified module.");
        }

        return entry;
    }

    private static uint[][] GetInputStorage(IReadOnlyList<WarpUInt32Buffer> inputs, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count != expectedCount)
        {
            throw new WarpHostException("WRPRUNTIME1004", "The input buffer count does not match the verified entry.");
        }

        var storage = new uint[inputs.Count][];
        for (int index = 0; index < inputs.Count; index++)
        {
            WarpUInt32Buffer input = inputs[index]
                ?? throw new ArgumentException("An input buffer cannot be null.", nameof(inputs));
            storage[index] = input.GetStorage();
        }

        return storage;
    }
}
