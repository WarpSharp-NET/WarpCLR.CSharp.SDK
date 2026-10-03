using Microsoft.CodeAnalysis;
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
