using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;
using WarpCLR.Verifier;

namespace WarpCLR.CSharp.Packaging.Tests;

[TestClass]
public sealed class WarpCLRPackageIntegrationTests
{
    private static readonly Lazy<WarpCLRPackageFixture> Fixture = new(
        WarpCLRPackageFixture.Create,
        LazyThreadSafetyMode.ExecutionAndPublication);

    [TestMethod]
    [FourBackends]
    public void Packaged_sdk_executes_the_complete_profile(WarpBackendKind backend)
    {
        AssertBackend(backend);
        WarpCLRPackageFixture fixture = Fixture.Value;
        byte[] assembly = fixture.ConsumerAssembly;
        WarpVerifiedModule verified = new WarpModuleVerifier().Verify(assembly);
        CollectionAssert.AreEqual(
            new[]
            {
                "Consumer.Kernels.Maximum",
                "Consumer.Kernels.Minimum",
                "Consumer.Kernels.Sum",
                "Consumer.Kernels.Transform",
            },
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
    public void Packaged_analyzer_rejects_unscoped_allocation(
        WarpBackendKind backend)
    {
        AssertBackend(backend);
        StringAssert.Contains(Fixture.Value.InvalidBuildOutput, "WCS2001");
    }

    [TestMethod]
    [FourBackends]
    public void Package_contains_only_the_required_sdk_assets(
        WarpBackendKind backend)
    {
        AssertBackend(backend);
        CollectionAssert.AreEqual(
            new[]
            {
                "analyzers/dotnet/cs/WarpCLR.CSharp.Analyzers.dll",
                "analyzers/dotnet/cs/WarpCLR.CSharp.Generators.dll",
                "build/WarpCLR.CSharp.targets",
                "lib/net10.0/WarpCLR.CSharp.dll",
                "tools/net10.0/any/WarpCLR.CSharp.Build.deps.json",
                "tools/net10.0/any/WarpCLR.CSharp.Build.dll",
                "tools/net10.0/any/WarpCLR.CSharp.Build.runtimeconfig.json",
                "tools/net10.0/any/WarpCLR.IR.dll",
                "tools/net10.0/any/WarpCLR.Verifier.dll",
            },
            Fixture.Value.PackageAssets.ToArray());
        StringAssert.Contains(
            Fixture.Value.IncrementalBuildOutput,
            "WarpCLR verified the finalized assembly.");
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        if (Fixture.IsValueCreated)
        {
            Fixture.Value.Dispose();
        }
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
