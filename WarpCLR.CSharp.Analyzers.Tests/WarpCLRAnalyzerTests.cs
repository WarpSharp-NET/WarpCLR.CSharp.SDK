using Microsoft.CodeAnalysis;
using System.Globalization;
using System.Text;
using WarpCLR.IR;

namespace WarpCLR.CSharp.Analyzers.Tests;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class WarpCLRAnalyzerTests
{
    [TestMethod]
    [FourBackends]
    public async Task ValidUnsignedMapHasNoDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform(
                    [WarpInput] uint value,
                    [WarpScalar] uint shift)
                {
                    uint mixed = (value * 33u) + 7u;
                    return ~(mixed >> (int)shift);
                }
            }
            """;

        await AssertNoDiagnosticsAsync(source).ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task InstanceEntryHasDeclarationDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public sealed class Kernels
            {
                [WarpEntryPoint]
                public uint Transform([WarpInput] uint value) => value;
            }
            """;

        await AssertIdsAsync(source, "WCS1001").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ScalarBeforeInputHasRoleDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform(
                    [WarpScalar] uint scalar,
                    [WarpInput] uint value) => value + scalar;
            }
            """;

        await AssertIdsAsync(source, "WCS1002").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ByReferenceParameterHasDeclarationDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] ref uint value) => value;
            }
            """;

        await AssertIdsAsync(source, "WCS1001").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task DivisionHasOperationDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Divide(
                    [WarpInput] uint value,
                    [WarpScalar] uint divisor) => value / divisor;
            }
            """;

        await AssertIdsAsync(source, "WCS1003").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ConditionalHasNoDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Select([WarpInput] uint value) =>
                    value == 0u ? 1u : value;
            }
            """;

        await AssertNoDiagnosticsAsync(source).ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ClosedStaticMethodCallHasNoDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => Rotate(value);

                private static uint Rotate(uint value) => (value << 1) | (value >> 31);
            }
            """;

        await AssertNoDiagnosticsAsync(source).ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ExternalMethodCallHasOperationDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using System.Numerics;
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) =>
                    BitOperations.RotateLeft(value, 1);
            }
            """;

        await AssertIdsAsync(source, "WCS1003").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task UnsupportedOperationInCalledMethodHasDiagnostic(
        WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => Divide(value, 3u);

                private static uint Divide(uint value, uint divisor) => value / divisor;
            }
            """;

        await AssertIdsAsync(source, "WCS1003").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task RecursiveMethodCallHasOperationDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => Recurse(value);

                private static uint Recurse(uint value) =>
                    value == 0u ? 0u : Recurse(value - 1u);
            }
            """;

        await AssertIdsAsync(source, "WCS1003").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task CheckedArithmeticHasOperationDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => checked(value + 1u);
            }
            """;

        await AssertIdsAsync(source, "WCS1003").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task EntryAllocationHasAllocationDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Allocate([WarpInput] uint value)
                {
                    uint[] values = new uint[1];
                    return value;
                }
            }
            """;

        await AssertContainsIdAsync(source, "WCS1004").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task SignedLocalHasOperationDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value)
                {
                    int signed = 1;
                    return value + (uint)signed;
                }
            }
            """;

        await AssertContainsIdAsync(source, "WCS1003").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task OrdinaryHostAllocationHasNoDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class HostCode
            {
                public static uint[] Create() => new uint[1];
            }
            """;

        await AssertNoDiagnosticsAsync(source).ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task PortableLoopsArgumentMutationAndBranchesHaveNoDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            public static class Kernels
            {
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
            }
            """;
        await AssertNoDiagnosticsAsync(source).ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task BooleanLocalControlUsesTheExistingPortableCILSubset(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Control([WarpInput] uint value)
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
            }
            """;
        await AssertNoDiagnosticsAsync(source).ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task CheckedIncrementIsRejectedEvenInsideAnAdmittedLoop(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Control([WarpInput] uint value)
                {
                    while (value != 0) { checked { value++; } }
                    return value;
                }
            }
            """;
        await AssertContainsIdAsync(source, "WCS1003").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task SignedLoopStateIsStillRejectedForEveryBackend(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Control([WarpInput] uint value)
                {
                    for (int index = 0; index < 4; index++) { value += (uint)index; }
                    return value;
                }
            }
            """;
        await AssertContainsIdAsync(source, "WCS1003").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ExplicitEntryTypeInitializationHasAnImplicitBehaviorDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            public static class Kernels
            {
                static Kernels() => throw new System.InvalidOperationException();
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => value;
            }
            """;
        await AssertIdsAsync(source, "WCS1005").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ImplicitEntryFieldInitializationHasAnImplicitBehaviorDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            public static class Kernels
            {
                public static readonly uint Seed = 7;
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => value;
            }
            """;
        await AssertIdsAsync(source, "WCS1005").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ExplicitHelperTypeInitializationHasAnImplicitBehaviorDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => Helper.Transform(value);
            }
            public static class Helper
            {
                static Helper() => throw new System.InvalidOperationException();
                public static uint Transform(uint value) => value + 1u;
            }
            """;
        await AssertIdsAsync(source, "WCS1005").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ImplicitHelperPropertyInitializationHasAnImplicitBehaviorDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => Helper.Transform(value);
            }
            public static class Helper
            {
                public static uint Seed { get; } = 7;
                public static uint Transform(uint value) => value + 1u;
            }
            """;
        await AssertIdsAsync(source, "WCS1005").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task SynchronizedEntryHasAnImplicitBehaviorDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using System.Runtime.CompilerServices;
            using WarpCLR.CSharp;
            public static class Kernels
            {
                [WarpEntryPoint, MethodImpl(MethodImplOptions.Synchronized)]
                public static uint Transform([WarpInput] uint value) => value;
            }
            """;
        await AssertIdsAsync(source, "WCS1005").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task SynchronizedHelperHasAnImplicitBehaviorDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using System.Runtime.CompilerServices;
            using WarpCLR.CSharp;
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => Helper(value);
                [MethodImpl(MethodImplOptions.Synchronized)]
                private static uint Helper(uint value) => value + 1u;
            }
            """;
        await AssertIdsAsync(source, "WCS1005").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task AModuleInitializerIsRejectedWhenTheModuleContainsKernelEntries(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using System.Runtime.CompilerServices;
            using WarpCLR.CSharp;
            public static class HostInitializer
            {
                [ModuleInitializer]
                public static void Initialize() { }
            }
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => value;
            }
            """;
        await AssertIdsAsync(source, "WCS1007").ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task AModuleInitializerInAnOrdinaryHostModuleHasNoWarpDiagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using System.Runtime.CompilerServices;
            public static class HostInitializer
            {
                [ModuleInitializer]
                public static void Initialize() { }
            }
            """;
        await AssertNoDiagnosticsAsync(source).ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task UnrelatedHostAndGeneratedCatalogInitializersRemainOutsideTheKernelClosure(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using System.Runtime.CompilerServices;
            using WarpCLR.CSharp;
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => value;
            }
            public static class HostCode
            {
                static HostCode() { }
                public static readonly uint Seed = 7;
                [MethodImpl(MethodImplOptions.Synchronized)]
                public static uint Transform(uint value) => value;
            }
            [CompilerGenerated]
            public static class WarpCLRKernelsEntries
            {
                public static WarpMapEntry Transform { get; } = new("Kernels.Transform", 1, 0);
            }
            """;
        await AssertNoDiagnosticsAsync(source).ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task ConstantsDoNotCreateAnImplicitTypeInitializer(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            public static class Kernels
            {
                public const uint Seed = 7;
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => value ^ Seed;
            }
            """;
        await AssertNoDiagnosticsAsync(source).ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task HelperDiscoveryAcceptsExactlyTheSharedPortableFunctionLimit(WarpBackendKind backend)
    {
        AssertBackend(backend);
        await AssertNoDiagnosticsAsync(CreateHelperChain(WarpCompilationAdmission.MaximumFunctionsPerEntry))
            .ConfigureAwait(false);
    }

    [TestMethod]
    [FourBackends]
    public async Task HelperDiscoveryRejectsBeyondTheSharedPortableFunctionLimitBeforeRecursing(WarpBackendKind backend)
    {
        AssertBackend(backend);
        Diagnostic[] diagnostics = (await AnalyzerTestHarness.AnalyzeAsync(
            CreateHelperChain(WarpCompilationAdmission.MaximumFunctionsPerEntry + 1)).ConfigureAwait(false)).ToArray();

        Assert.HasCount(1, diagnostics);
        Assert.AreEqual("WCS1006", diagnostics[0].Id, StringComparer.Ordinal);
        string limit = WarpCompilationAdmission.MaximumFunctionsPerEntry.ToString(CultureInfo.InvariantCulture);
        StringAssert.Contains(diagnostics[0].GetMessage(CultureInfo.InvariantCulture), limit, StringComparison.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public async Task ReusingDiscoveredHelpersDoesNotSpendTheFunctionBudgetAgain(WarpBackendKind backend)
    {
        AssertBackend(backend);
        await AssertNoDiagnosticsAsync(CreateHelperChain(WarpCompilationAdmission.MaximumFunctionsPerEntry, reuseHelpers: true))
            .ConfigureAwait(false);
    }

    private static string CreateHelperChain(int count, bool reuseHelpers = false)
    {
        var source = new StringBuilder("using WarpCLR.CSharp; public static class Kernels { ");
        source.Append("[WarpEntryPoint] public static uint Transform([WarpInput] uint value) => Helper0(value)");
        if (reuseHelpers)
        {
            source.Append(" ^ Helper0(value + 1u)");
        }

        source.Append(';');
        for (int index = 0; index < count; index++)
        {
            source.Append("private static uint Helper");
            source.Append(index.ToString(CultureInfo.InvariantCulture));
            source.Append("(uint value) => ");
            if (index + 1 == count)
            {
                source.Append("value + 1u");
            }
            else
            {
                source.Append("Helper");
                source.Append((index + 1).ToString(CultureInfo.InvariantCulture));
                source.Append("(value)");
            }

            source.Append(';');
        }

        return source.Append('}').ToString();
    }

    private static async Task AssertIdsAsync(string source, params string[] expected)
    {
        Diagnostic[] diagnostics = (await AnalyzerTestHarness.AnalyzeAsync(source).ConfigureAwait(false)).ToArray();
        string[] actual = diagnostics
            .Select(diagnostic => diagnostic.Id)
            .ToArray();
        CollectionAssert.AreEqual(expected, actual, Describe(diagnostics));
    }

    private static async Task AssertContainsIdAsync(string source, string expected)
    {
        Diagnostic[] diagnostics = (await AnalyzerTestHarness.AnalyzeAsync(source).ConfigureAwait(false)).ToArray();
        Assert.IsTrue(diagnostics.Any(diagnostic => string.Equals(diagnostic.Id, expected, StringComparison.Ordinal)));
    }

    private static async Task AssertNoDiagnosticsAsync(string source)
    {
        Diagnostic[] diagnostics = (await AnalyzerTestHarness.AnalyzeAsync(source).ConfigureAwait(false)).ToArray();
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(
                diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"));

    private static void AssertBackend(WarpBackendKind backend) =>
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
}
