using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;
using WarpCLR.Verifier;

namespace WarpCLR.CSharp.Packaging.Tests;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class WarpCLRPackageIntegrationTests
{
    private static readonly string[] ExpectedEntries =
    [
        "Consumer.Kernels.Maximum",
        "Consumer.Kernels.Minimum",
        "Consumer.Kernels.Select",
        "Consumer.Kernels.Sum",
        "Consumer.Kernels.Transform",
    ];

    private static readonly string[] ExpectedSdkAssets =
    [
        "analyzers/dotnet/cs/WarpCLR.CSharp.Analyzers.dll",
        "analyzers/dotnet/cs/WarpCLR.CSharp.Generators.dll",
        "build/WarpCLR.CSharp.targets",
        "lib/net10.0/WarpCLR.CSharp.dll",
        "tools/net10.0/any/WarpCLR.CSharp.Build.deps.json",
        "tools/net10.0/any/WarpCLR.CSharp.Build.dll",
        "tools/net10.0/any/WarpCLR.CSharp.Build.runtimeconfig.json",
        "tools/net10.0/any/WarpCLR.IR.dll",
        "tools/net10.0/any/WarpCLR.Verifier.dll",
    ];

    private static WarpCLRPackageFixture? sharedFixture;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        sharedFixture = await WarpCLRPackageFixture.CreateAsync().ConfigureAwait(false);
    }

    private static WarpCLRPackageFixture Fixture => sharedFixture
        ?? throw new InvalidOperationException("The packaging fixture was not initialized.");

    [TestMethod]
    [FourBackends]
    public void PackagedSdkExecutesTheCompleteProfile(WarpBackendKind backend)
    {
        AssertBackend(backend);
        WarpCLRPackageFixture fixture = Fixture;
        byte[] assembly = fixture.ConsumerAssembly;
        WarpVerifiedModule verified = new WarpModuleVerifier().Verify(assembly);
        CollectionAssert.AreEqual(
            ExpectedEntries,
            verified.Entries.Select(entry => entry.Identity).ToArray());

        WarpAotPackage package = WarpCLRCompiler.CompilePackage(assembly);
        string packageDirectory = fixture.CreateRuntimeDirectory(backend.ToString());
        package.WriteToDirectory(packageDirectory);
        WarpCLRProgram program = WarpCLRProgram.Load(assembly, packageDirectory);
        WarpCLRSession session = program.CreateDevelopmentSession(backend);
        Assert.AreEqual(backend, session.Backend);
        Assert.AreEqual(
            WarpDevelopmentExecutionMode.SemanticEmulation,
            session.Mode);

        WarpUInt32Buffer input = WarpUInt32Buffer.From(
            0u,
            1u,
            uint.MaxValue,
            0x80000000u,
            17u);
        WarpUInt32Buffer mapped = session.Dispatch(
            new WarpMapEntry("Consumer.Kernels.Transform", 1, 1),
            [input],
            [7u]);
        CollectionAssert.AreEqual(
            input.Select(value => unchecked((value * 33u) + 7u)).ToArray(),
            mapped.ToArray());

        const uint threshold = 17u;
        WarpUInt32Buffer selected = session.Dispatch(
            new WarpMapEntry("Consumer.Kernels.Select", 1, 1),
            [input],
            [threshold]);
        CollectionAssert.AreEqual(
            input
                .Select(
                    value => value <= threshold
                        ? unchecked(value + 1u)
                        : unchecked(value - 1u))
                .ToArray(),
            selected.ToArray());

        AssertReductionResults(session, input);
    }

    private static void AssertReductionResults(WarpCLRSession session, WarpUInt32Buffer input)
    {
        Assert.AreEqual(
            WrappingSum(input),
            session.Reduce(
                new WarpReductionEntry(
                    "Consumer.Kernels.Sum",
                    1,
                    0,
                    WarpExecution.ReduceWrappingSum),
                [input]));
        Assert.AreEqual(
            input.Min(),
            session.Reduce(
                new WarpReductionEntry(
                    "Consumer.Kernels.Minimum",
                    1,
                    0,
                    WarpExecution.ReduceMinimum),
                [input]));
        Assert.AreEqual(
            input.Max(),
            session.Reduce(
                new WarpReductionEntry(
                    "Consumer.Kernels.Maximum",
                    1,
                    0,
                    WarpExecution.ReduceMaximum),
                [input]));
    }

    [TestMethod]
    [FourBackends]
    public void PackagedAnalyzerRejectsNonportableOperation(
        WarpBackendKind backend)
    {
        AssertBackend(backend);
        StringAssert.Contains(Fixture.InvalidBuildOutput, "WCS1003", StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void PackageContainsOnlyTheRequiredSdkAssets(
        WarpBackendKind backend)
    {
        AssertBackend(backend);
        CollectionAssert.AreEqual(
            ExpectedSdkAssets,
            Fixture.PackageAssets.ToArray());
        StringAssert.Contains(
            Fixture.IncrementalBuildOutput,
            "WarpCLR verified the finalized assembly.", StringComparison.Ordinal);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        sharedFixture?.Dispose();
        sharedFixture = null;
    }

    private static uint WrappingSum(IEnumerable<uint> values)
    {
        uint result = 0;
        foreach (uint value in values)
        {
            result = unchecked(result + value);
        }

        return result;
    }

    private static void AssertBackend(WarpBackendKind backend) =>
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
}
