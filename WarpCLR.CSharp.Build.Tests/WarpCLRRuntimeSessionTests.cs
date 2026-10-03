using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using WarpCLR.CSharp.Analyzers;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.CSharp.Build.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "MSTest discovers and instantiates this fixture through reflection.")]
internal sealed class WarpCLRRuntimeSessionTests
{
    private static RuntimeSourceFixture? sourceFixture;

    private static RuntimeSourceFixture Fixture => sourceFixture ?? throw new InvalidOperationException("The source fixture has not been initialized.");

    [ClassInitialize]
    public static async Task Initialize(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        sourceFixture = await RuntimeSourceFixture.CreateAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public async Task GeneratedSourceHandlesExecuteNativeCoreCLRNestedHelpersAndLoops()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession session = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR, TestOptions());
        await using var sessionLease = session.ConfigureAwait(false);
        Assert.AreEqual(WarpBackendKind.CoreCLR, session.Backend);
        Assert.AreEqual(WarpRuntimeContextState.Ready, session.State);
        WarpUInt32Buffer input = WarpUInt32Buffer.From(0, 1, 2, 3, 31, 0x80000000, uint.MaxValue);
        WarpUInt32Buffer output = await session.DispatchAsync(fixture.Nested, [input], [7]).ConfigureAwait(false);
        CollectionAssert.AreEqual(input.Select(value => fixture.Evaluate(value, 7)).ToArray(), output.ToArray());
        Assert.AreEqual(1L, session.JitStatistics.CompilationCount);
        Assert.AreEqual(1, session.JitStatistics.MemoryEntryCount);
        Assert.AreEqual(WarpRuntimeContextState.Ready, session.State);
    }

    [TestMethod]
    public async Task MillionElementWorkloadHasNativeMapAndScalableReductionProof()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession session = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR, TestOptions());
        await using var sessionLease = session.ConfigureAwait(false);
        uint[] values = Enumerable.Range(0, 1_000_000).Select(index => unchecked((uint)(index * 786433))).ToArray();
        var input = new WarpUInt32Buffer(values);
        const uint scalar = 9;
        var stopwatch = Stopwatch.StartNew();
        WarpUInt32Buffer output = await session.DispatchAsync(fixture.Nested, [input], [scalar]).ConfigureAwait(false);
        uint[] expected = values.Select(value => fixture.Evaluate(value, scalar)).ToArray();
        CollectionAssert.AreEqual(expected, output.ToArray());
        uint expectedSum = expected.Aggregate(0u, (sum, value) => unchecked(sum + value));
        uint actualSum = await session.ReduceAsync(fixture.Sum, [input], [scalar]).ConfigureAwait(false);
        Assert.AreEqual(expectedSum, actualSum);
        Assert.AreEqual(2L, session.JitStatistics.CompilationCount);
        Assert.AreEqual(WarpRuntimeContextState.Ready, session.State);
        TestContext.WriteLine($"Native verified-source CoreCLR map + reduction: {values.Length} workers, {stopwatch.Elapsed}.");
    }

    [TestMethod]
    public async Task FrontendControlFlowAndBooleanLocalsAreVerifiedAndRunNatively()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession session = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR, TestOptions());
        await using var sessionLease = session.ConfigureAwait(false);
        WarpUInt32Buffer input = WarpUInt32Buffer.From(0, 1, 2, 3, 5, 7, 15, 31);
        WarpUInt32Buffer control = await session.DispatchAsync(fixture.Control, [input]).ConfigureAwait(false);
        WarpUInt32Buffer boolean = await session.DispatchAsync(fixture.Boolean, [input]).ConfigureAwait(false);
        CollectionAssert.AreEqual(input.Select(fixture.EvaluateControl).ToArray(), control.ToArray());
        CollectionAssert.AreEqual(input.Select(fixture.EvaluateBoolean).ToArray(), boolean.ToArray());
        Assert.AreEqual(2L, session.JitStatistics.CompilationCount);
    }

    [TestMethod]
    public async Task ReductionDescriptorsPreserveAllRegisteredModesAndEmptyIdentities()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession session = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR, TestOptions());
        await using var sessionLease = session.ConfigureAwait(false);
        WarpUInt32Buffer input = WarpUInt32Buffer.From(17, uint.MaxValue, 0, 0x80000000, 1);
        Assert.AreEqual(0u, await session.ReduceAsync(fixture.Sum, [new WarpUInt32Buffer(0)], [7]).ConfigureAwait(false));
        Assert.AreEqual(uint.MaxValue, await session.ReduceAsync(fixture.Minimum, [new WarpUInt32Buffer(0)]).ConfigureAwait(false));
        Assert.AreEqual(0u, await session.ReduceAsync(fixture.Maximum, [new WarpUInt32Buffer(0)]).ConfigureAwait(false));
        Assert.AreEqual(input.Min(), await session.ReduceAsync(fixture.Minimum, [input]).ConfigureAwait(false));
        Assert.AreEqual(input.Max(), await session.ReduceAsync(fixture.Maximum, [input]).ConfigureAwait(false));
    }

    [TestMethod]
    public void TrustedLoadRejectsUnapprovedBytesAndNeverSelectsEmulation()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpHostException denied = Assert.ThrowsExactly<WarpHostException>(() =>
            WarpCLRProgram.LoadTrusted(fixture.AssemblyBytes, new WarpModuleTrust([])));
        Assert.AreEqual("WRPRUNTIME1000", denied.Code, StringComparer.Ordinal);
        Assert.ThrowsExactly<WarpHostException>(() => fixture.Program.CreateDevelopmentSession(WarpBackendKind.CoreCLR));
        byte[] altered = fixture.AssemblyBytes.ToArray();
        altered[^1] ^= 1;
        Assert.ThrowsExactly<WarpHostException>(() => WarpCLRProgram.LoadTrusted(altered, fixture.Trust));
    }

    [TestMethod]
    public async Task DescriptorShapeAndExecutionModeErrorsDoNotTaintTheSession()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession session = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR, TestOptions());
        await using var sessionLease = session.ConfigureAwait(false);
        WarpUInt32Buffer input = WarpUInt32Buffer.From(3);
        await Assert.ThrowsExactlyAsync<WarpHostException>(() => session.DispatchAsync(
            new WarpMapEntry(fixture.Nested.Identity, 1, 0), [input])).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<WarpHostException>(() => session.DispatchAsync(
            new WarpMapEntry(fixture.Sum.Identity, 1, 1), [input], [7])).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<WarpHostException>(async () => await session.ReduceAsync(
            new WarpReductionEntry(fixture.Sum.Identity, 1, 1, WarpExecution.ReduceMinimum), [input], [7]).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.AreEqual(WarpRuntimeContextState.Ready, session.State);
        Assert.AreEqual(0L, session.JitStatistics.CompilationCount);
        Assert.AreEqual(fixture.Evaluate(3, 7), (await session.DispatchAsync(fixture.Nested, [input], [7]).ConfigureAwait(false))[0]);
    }

    [TestMethod]
    public async Task LogicalFaultSuppressesOutputAndTaintsOnlyItsSession()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession failed = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR,
            TestOptions() with { MaximumStepsPerWorker = 100 });
        await using var failedLease = failed.ConfigureAwait(false);
        WarpRuntimeFaultException fault = await Assert.ThrowsExactlyAsync<WarpRuntimeFaultException>(() =>
            failed.DispatchAsync(fixture.Infinite, [WarpUInt32Buffer.From(0, 1, 0)])).ConfigureAwait(false);
        Assert.AreEqual(WarpRuntimeFaultKind.StepLimit, fault.Kind);
        Assert.AreEqual(1, fault.WorkerIndex);
        Assert.AreEqual(WarpRuntimeContextState.Faulted, failed.State);
        await Assert.ThrowsExactlyAsync<WarpHostException>(() => failed.DispatchAsync(fixture.Nested, [WarpUInt32Buffer.From(0)], [7])).ConfigureAwait(false);
        WarpCLRRuntimeSession healthy = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR, TestOptions());
        await using var healthyLease = healthy.ConfigureAwait(false);
        Assert.AreEqual(fixture.Evaluate(0, 7), (await healthy.DispatchAsync(fixture.Nested, [WarpUInt32Buffer.From(0)], [7]).ConfigureAwait(false))[0]);
    }

    [TestMethod]
    public async Task LogicalCallStackFaultIsPropagatedThroughTheTypedApi()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession session = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR,
            TestOptions() with { MaximumCallDepth = 1 });
        await using var sessionLease = session.ConfigureAwait(false);
        WarpRuntimeFaultException fault = await Assert.ThrowsExactlyAsync<WarpRuntimeFaultException>(() =>
            session.DispatchAsync(fixture.Nested, [WarpUInt32Buffer.From(3)], [7])).ConfigureAwait(false);
        Assert.AreEqual(WarpRuntimeFaultKind.CallDepth, fault.Kind);
        Assert.AreEqual(0, fault.WorkerIndex);
        Assert.AreEqual(WarpRuntimeContextState.Faulted, session.State);
    }

    [TestMethod]
    public async Task TypedBuffersAndScalarsAreSnapshottedBeforeAQueuedDispatch()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession session = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR,
            TestOptions() with { MaximumConcurrentDispatches = 1, MaximumStepsPerWorker = long.MaxValue });
        await using var sessionLease = session.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        Task<WarpUInt32Buffer> blocking = session.DispatchAsync(fixture.Infinite, [WarpUInt32Buffer.From(1)], cancellationToken: cancellation.Token);
        Assert.IsTrue(SpinWait.SpinUntil(() => session.JitStatistics.CompilationCount != 0, TimeSpan.FromSeconds(10)));
        WarpUInt32Buffer input = WarpUInt32Buffer.From(3, 7, 31);
        uint[] scalars = [7];
        uint[] expected = input.Select(value => fixture.Evaluate(value, 7)).ToArray();
        Task<WarpUInt32Buffer> queued = session.DispatchAsync(fixture.Nested, [input], scalars);
        input.Span.Fill(uint.MaxValue);
        scalars[0] = uint.MaxValue;
        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await blocking.ConfigureAwait(false);
            Assert.Fail("The blocking dispatch was expected to be cancelled.");
        }
        catch (OperationCanceledException)
        {
            Assert.IsTrue(blocking.IsCanceled);
        }

        CollectionAssert.AreEqual(expected, (await queued.ConfigureAwait(false)).ToArray());
        Assert.AreEqual(WarpRuntimeContextState.Ready, session.State);
    }

    [TestMethod]
    public async Task ResourceAdmissionAndPrecancellationPublishNoOutputAndPreserveReadyState()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession session = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR,
            TestOptions() with { MaximumBufferBytes = 1 });
        await using var sessionLease = session.ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<WarpHostException>(() => session.DispatchAsync(fixture.Nested, [WarpUInt32Buffer.From(3)], [7])).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            session.DispatchAsync(fixture.Nested, [WarpUInt32Buffer.From(3)], [7], cancellation.Token)).ConfigureAwait(false);
        Assert.AreEqual(WarpRuntimeContextState.Ready, session.State);
        Assert.AreEqual(0L, session.JitStatistics.CompilationCount);
    }

    [TestMethod]
    public async Task AsyncDisposalDrainsCancelledExecutionAndIsIdempotent()
    {
        RuntimeSourceFixture fixture = Fixture;
        WarpCLRRuntimeSession session = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR,
            TestOptions() with { MaximumStepsPerWorker = long.MaxValue });
        await using var sessionLease = session.ConfigureAwait(false);
        Task<WarpUInt32Buffer> execution = session.DispatchAsync(fixture.Infinite, [WarpUInt32Buffer.From(1)]);
        Assert.IsTrue(SpinWait.SpinUntil(() => session.JitStatistics.CompilationCount != 0, TimeSpan.FromSeconds(10)));
        await session.DisposeAsync().ConfigureAwait(false);
        try
        {
            await execution.ConfigureAwait(false);
            Assert.Fail("Disposal was expected to cancel the dispatch.");
        }
        catch (OperationCanceledException)
        {
            Assert.IsTrue(execution.IsCanceled);
        }

        await session.DisposeAsync().ConfigureAwait(false);
        Assert.AreEqual(WarpRuntimeContextState.Disposed, session.State);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => session.DispatchAsync(fixture.Nested, [WarpUInt32Buffer.From(0)], [7])).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ConcurrentSessionsShareOneCompilationWithoutSharedBufferStorage()
    {
        RuntimeSourceFixture fixture = Fixture;
        var cache = new WarpJitCache();
        WarpCLRRuntimeSession first = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR, TestOptions(), cache);
        await using var firstLease = first.ConfigureAwait(false);
        WarpCLRRuntimeSession second = fixture.Program.CreateRuntimeSession(WarpBackendKind.CoreCLR, TestOptions(), cache);
        await using var secondLease = second.ConfigureAwait(false);
        WarpUInt32Buffer input = WarpUInt32Buffer.From(1, 2, 3);
        Task<WarpUInt32Buffer> firstTask = first.DispatchAsync(fixture.Nested, [input], [7]);
        Task<WarpUInt32Buffer> secondTask = second.DispatchAsync(fixture.Nested, [input], [7]);
        WarpUInt32Buffer[] results = await Task.WhenAll(firstTask, secondTask).ConfigureAwait(false);
        Assert.AreEqual(1L, cache.Statistics.CompilationCount);
        results[0][0] = 0;
        Assert.AreEqual(fixture.Evaluate(1, 7), results[1][0]);
        Assert.AreEqual(1u, input[0]);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        sourceFixture?.Dispose();
        sourceFixture = null;
    }

    public TestContext TestContext { get; set; } = null!;

    private static WarpRuntimeOptions TestOptions() => new()
    {
        MaximumCallDepth = 4,
        MaximumParallelWorkers = Math.Min(Environment.ProcessorCount, 8),
        MaximumResidentWorkers = 256,
    };

    private sealed class RuntimeSourceFixture : IDisposable
    {
        private const string Source = """
            using WarpCLR.CSharp;
            namespace RuntimeSdkSample;
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Nested([WarpInput] uint value, [WarpScalar] uint scalar) =>
                    Fold(value, scalar) ^ Fold(value >> 2, scalar + 3u);

                [WarpEntryPoint(WarpExecution.ReduceWrappingSum)]
                public static uint Sum([WarpInput] uint value, [WarpScalar] uint scalar) => Nested(value, scalar);

                [WarpEntryPoint(WarpExecution.ReduceMinimum)]
                public static uint Minimum([WarpInput] uint value) => value;

                [WarpEntryPoint(WarpExecution.ReduceMaximum)]
                public static uint Maximum([WarpInput] uint value) => value;

                [WarpEntryPoint]
                public static uint Infinite([WarpInput] uint value)
                {
                    while (value != 0) { value += 2u; }
                    return value;
                }

                [WarpEntryPoint]
                public static uint Control([WarpInput] uint value)
                {
                    uint result = 0;
                    value &= 15u;
                    while (value != 0)
                    {
                        value--;
                        if (value == 9) continue;
                        if (value == 2) break;
                        result += value;
                    }
                    do { ++result; } while (result < 3);
                    for (uint index = 0; index < 4; index++) { result ^= index; }
                    value = value + result;
                Again:
                    if (value > 64) { value -= 64; goto Again; }
                    return value;
                }

                [WarpEntryPoint]
                public static uint Boolean([WarpInput] uint value)
                {
                    bool active = value != 0;
                    bool alternate = false;
                    while (active && !alternate)
                    {
                        value--;
                        active = value != 0;
                        alternate ^= value == 4;
                    }
                    bool selected = active ? alternate : !alternate;
                    return (selected || (active & alternate)) ? value + 1u : value;
                }

                private static uint Fold(uint value, uint scalar)
                {
                    uint result = value ^ scalar;
                    uint outer = value & 3u;
                    while (outer != 0)
                    {
                        for (uint inner = (scalar & 7u) + 1u; inner != 0; inner--)
                        {
                            result = (result * 33u) + outer + inner;
                        }
                        outer--;
                    }
                    return Rotate(result);
                }
                private static uint Rotate(uint value) => (value << 1) | (value >> 31);
            }
            """;

        private readonly AssemblyLoadContext loadContext;
        private readonly Func<uint, uint, uint> sourceEvaluation;
        private readonly Func<uint, uint> controlEvaluation;
        private readonly Func<uint, uint> booleanEvaluation;

        private RuntimeSourceFixture(byte[] assemblyBytes, AssemblyLoadContext loadContext, Assembly assembly)
        {
            AssemblyBytes = assemblyBytes;
            Trust = new WarpModuleTrust([Convert.ToHexString(SHA256.HashData(assemblyBytes))]);
            Program = WarpCLRProgram.LoadTrusted(assemblyBytes, Trust);
            this.loadContext = loadContext;
            Type handles = assembly.GetType("RuntimeSdkSample.WarpCLRKernelsEntries")!;
            Nested = (WarpMapEntry)handles.GetProperty("Nested")!.GetValue(null)!;
            Infinite = (WarpMapEntry)handles.GetProperty("Infinite")!.GetValue(null)!;
            Sum = (WarpReductionEntry)handles.GetProperty("Sum")!.GetValue(null)!;
            Minimum = (WarpReductionEntry)handles.GetProperty("Minimum")!.GetValue(null)!;
            Maximum = (WarpReductionEntry)handles.GetProperty("Maximum")!.GetValue(null)!;
            Control = (WarpMapEntry)handles.GetProperty("Control")!.GetValue(null)!;
            Boolean = (WarpMapEntry)handles.GetProperty("Boolean")!.GetValue(null)!;
            Type kernels = assembly.GetType("RuntimeSdkSample.Kernels")!;
            sourceEvaluation = kernels.GetMethod("Nested")!.CreateDelegate<Func<uint, uint, uint>>();
            controlEvaluation = kernels.GetMethod("Control")!.CreateDelegate<Func<uint, uint>>();
            booleanEvaluation = kernels.GetMethod("Boolean")!.CreateDelegate<Func<uint, uint>>();
        }

        public byte[] AssemblyBytes { get; }

        public WarpModuleTrust Trust { get; }

        public WarpCLRProgram Program { get; }

        public WarpMapEntry Nested { get; }

        public WarpMapEntry Infinite { get; }

        public WarpMapEntry Control { get; }

        public WarpMapEntry Boolean { get; }

        public WarpReductionEntry Sum { get; }

        public WarpReductionEntry Minimum { get; }

        public WarpReductionEntry Maximum { get; }

        public uint Evaluate(uint value, uint scalar) => sourceEvaluation(value, scalar);

        public uint EvaluateControl(uint value) => controlEvaluation(value);

        public uint EvaluateBoolean(uint value) => booleanEvaluation(value);

        public static async Task<RuntimeSourceFixture> CreateAsync()
        {
            WarpCLRGeneratorTestResult generated = WarpCLRGeneratorTestHarness.Run("RuntimeSourceFixture", Source);
            Assert.IsEmpty(generated.DriverDiagnostics);
            var diagnostics = await generated.OutputCompilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new WarpCLRAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);
            Assert.IsEmpty(diagnostics, string.Join(Environment.NewLine, diagnostics));
            using var original = new MemoryStream();
            EmitResult emitted = generated.OutputCompilation.Emit(original);
            Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            WarpCLRAssemblyFinalization finalized = WarpCLRAssemblyFinalizer.FinalizeAssembly(original.ToArray());
            Assert.IsTrue(finalized.Changed);
            Assert.IsNotNull(finalized.Module);
            Assert.HasCount(7, finalized.Module.Entries);
            var assemblyContext = new AssemblyLoadContext("WarpCLRRuntimeSdkFixture", isCollectible: true);
            using var bytes = new MemoryStream(finalized.AssemblyBytes, writable: false);
            return new RuntimeSourceFixture(finalized.AssemblyBytes, assemblyContext, assemblyContext.LoadFromStream(bytes));
        }

        public void Dispose() => loadContext.Unload();
    }
}
