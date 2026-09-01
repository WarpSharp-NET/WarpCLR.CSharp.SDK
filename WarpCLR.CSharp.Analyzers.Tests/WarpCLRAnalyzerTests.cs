using Microsoft.CodeAnalysis;
using WarpCLR.IR;

namespace WarpCLR.CSharp.Analyzers.Tests;

[TestClass]
public sealed class WarpCLRAnalyzerTests
{
    [TestMethod]
    [FourBackends]
    public void Valid_unsigned_map_has_no_diagnostic(WarpBackendKind backend)
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

        AssertNoDiagnostics(source);
    }

    [TestMethod]
    [FourBackends]
    public void Instance_entry_has_declaration_diagnostic(WarpBackendKind backend)
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

        AssertIds(source, "WCS1001");
    }

    [TestMethod]
    [FourBackends]
    public void Scalar_before_input_has_role_diagnostic(WarpBackendKind backend)
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

        AssertIds(source, "WCS1002");
    }

    [TestMethod]
    [FourBackends]
    public void By_reference_parameter_has_declaration_diagnostic(WarpBackendKind backend)
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

        AssertIds(source, "WCS1001");
    }

    [TestMethod]
    [FourBackends]
    public void Division_has_operation_diagnostic(WarpBackendKind backend)
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

        AssertIds(source, "WCS1003");
    }

    [TestMethod]
    [FourBackends]
    public void Conditional_has_operation_diagnostic(WarpBackendKind backend)
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

        AssertIds(source, "WCS1003");
    }

    [TestMethod]
    [FourBackends]
    public void Method_call_has_operation_diagnostic(WarpBackendKind backend)
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

        AssertIds(source, "WCS1003");
    }

    [TestMethod]
    [FourBackends]
    public void Checked_arithmetic_has_operation_diagnostic(WarpBackendKind backend)
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

        AssertIds(source, "WCS1003");
    }

    [TestMethod]
    [FourBackends]
    public void Entry_allocation_has_allocation_diagnostic(WarpBackendKind backend)
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

        AssertContainsId(source, "WCS1004");
    }

    [TestMethod]
    [FourBackends]
    public void Signed_local_has_operation_diagnostic(WarpBackendKind backend)
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

        AssertContainsId(source, "WCS1003");
    }

    [TestMethod]
    [FourBackends]
    public void Scope_without_using_has_scope_diagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class HostCode
            {
                public static void Execute()
                {
                    WarpScope scope = WarpCLRMemory.Scope(64);
                    scope.Dispose();
                }
            }
            """;

        AssertIds(source, "WCS2001");
    }

    [TestMethod]
    [FourBackends]
    public void Scope_with_using_has_no_diagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class HostCode
            {
                public static uint Execute()
                {
                    using WarpScope scope = WarpCLRMemory.Scope(64);
                    WarpScopedUInt32Array values = scope.AllocateUInt32Array(1);
                    values[0] = 17u;
                    return values[0];
                }
            }
            """;

        AssertNoDiagnostics(source);
    }

    [TestMethod]
    [FourBackends]
    public void Scope_return_has_scope_and_escape_diagnostics(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class HostCode
            {
                public static WarpScope Create() => WarpCLRMemory.Scope(64);
            }
            """;

        AssertIds(source, "WCS2001", "WCS2002");
    }

    [TestMethod]
    [FourBackends]
    public void Ordinary_host_allocation_has_no_diagnostic(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            public static class HostCode
            {
                public static uint[] Create() => new uint[1];
            }
            """;

        AssertNoDiagnostics(source);
    }

    private static void AssertIds(string source, params string[] expected)
    {
        Diagnostic[] diagnostics = AnalyzerTestHarness.Analyze(source).ToArray();
        string[] actual = diagnostics
            .Select(diagnostic => diagnostic.Id)
            .ToArray();
        CollectionAssert.AreEqual(expected, actual, Describe(diagnostics));
    }

    private static void AssertContainsId(string source, string expected)
    {
        Assert.IsTrue(
            AnalyzerTestHarness.Analyze(source)
                .Any(diagnostic => diagnostic.Id == expected));
    }

    private static void AssertNoDiagnostics(string source)
    {
        Diagnostic[] diagnostics = AnalyzerTestHarness.Analyze(source).ToArray();
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(
                diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}"));

    private static void AssertBackend(WarpBackendKind backend) =>
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
}
