using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.CSharp.Generators.Tests;

[TestClass]
public sealed class WarpCLRGeneratorTests
{
    [TestMethod]
    [FourBackends]
    public void Map_entry_emits_manifest_and_catalog(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using System.Reflection;
            using WarpCLR.CSharp;

            [assembly: AssemblyVersion("2.3.4.0")]

            namespace Demo;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform(
                    [WarpInput] uint value,
                    [WarpInput] uint other,
                    [WarpScalar] uint mask) => (value + other) ^ mask;
            }
            """;

        WarpCLRGeneratorTestResult result = RunValid(
            $"GeneratorMap{backend}",
            source);
        string manifest = RequireManifest(result);
        using JsonDocument document = JsonDocument.Parse(manifest);
        JsonElement root = document.RootElement;
        Assert.AreEqual($"GeneratorMap{backend}", root.GetProperty("producer").GetString());
        Assert.AreEqual("2.3.4.0", root.GetProperty("producerVersion").GetString());

        JsonElement entry = root.GetProperty("entries")[0];
        Assert.AreEqual("Demo.Kernels", entry.GetProperty("type").GetString());
        Assert.AreEqual("Transform", entry.GetProperty("method").GetString());
        Assert.AreEqual("map", entry.GetProperty("execution").GetString());
        CollectionAssert.AreEqual(
            new[] { "input", "input", "scalar" },
            entry.GetProperty("parameterRoles")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray());
        AssertUppercaseHash(entry.GetProperty("graphHash").GetString());

        INamedTypeSymbol? catalog = result.OutputCompilation
            .GetTypeByMetadataName("Demo.WarpCLRKernelsEntries");
        Assert.IsNotNull(catalog);
        IPropertySymbol property = catalog.GetMembers("Transform")
            .OfType<IPropertySymbol>()
            .Single();
        Assert.AreEqual(
            "WarpCLR.CSharp.WarpMapEntry",
            property.Type.ToDisplayString());
        StringAssert.Contains(
            result.GeneratedSources.Single().SourceText.ToString(),
            "new global::WarpCLR.CSharp.WarpMapEntry(\"Demo.Kernels.Transform\", 2, 1)");
        AssertCanonicalManifestReachesGraphHash(result);
    }

    [TestMethod]
    [FourBackends]
    public void All_execution_modes_emit_exact_names(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            namespace Demo;

            public static class Reductions
            {
                [WarpEntryPoint(WarpExecution.ReduceWrappingSum)]
                public static uint WrappingSum([WarpInput] uint value) => value;

                [WarpEntryPoint(WarpExecution.Map)]
                public static uint Map([WarpInput] uint value) => value;

                [WarpEntryPoint(WarpExecution.ReduceMinimum)]
                public static uint Minimum([WarpInput] uint value) => value;

                [WarpEntryPoint(WarpExecution.ReduceMaximum)]
                public static uint Maximum([WarpInput] uint value) => value;
            }
            """;

        WarpCLRGeneratorTestResult result = RunValid(
            $"GeneratorModes{backend}",
            source);
        using JsonDocument document = JsonDocument.Parse(RequireManifest(result));
        JsonElement entries = document.RootElement.GetProperty("entries");
        CollectionAssert.AreEqual(
            new[] { "Map", "Maximum", "Minimum", "WrappingSum" },
            entries.EnumerateArray()
                .Select(entry => entry.GetProperty("method").GetString())
                .ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "map",
                "reduce-maximum",
                "reduce-minimum",
                "reduce-wrapping-sum",
            },
            entries.EnumerateArray()
                .Select(entry => entry.GetProperty("execution").GetString())
                .ToArray());

        INamedTypeSymbol catalog = result.OutputCompilation
            .GetTypeByMetadataName("Demo.WarpCLRReductionsEntries")!;
        Assert.AreEqual(
            "WarpCLR.CSharp.WarpMapEntry",
            catalog.GetMembers("Map").OfType<IPropertySymbol>().Single().Type.ToDisplayString());
        foreach (string name in new[] { "Maximum", "Minimum", "WrappingSum" })
        {
            Assert.AreEqual(
                "WarpCLR.CSharp.WarpReductionEntry",
                catalog.GetMembers(name).OfType<IPropertySymbol>().Single().Type.ToDisplayString());
        }

        AssertCanonicalManifestReachesGraphHash(result);
    }

    [TestMethod]
    [FourBackends]
    public void Entry_order_is_source_order_independent(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string first = """
            using WarpCLR.CSharp;
            namespace Demo;
            public static class Zeta
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => value + 1u;
            }
            """;
        const string second = """
            using WarpCLR.CSharp;
            namespace Demo;
            public static class Alpha
            {
                [WarpEntryPoint]
                public static uint Transform([WarpInput] uint value) => value * 3u;
            }
            """;

        WarpCLRGeneratorTestResult forward = RunValid(
            $"GeneratorOrder{backend}",
            first,
            second);
        WarpCLRGeneratorTestResult reverse = RunValid(
            $"GeneratorOrder{backend}",
            second,
            first);

        Assert.AreEqual(RequireManifest(forward), RequireManifest(reverse));
        Assert.AreEqual(
            forward.GeneratedSources.Single().SourceText.ToString(),
            reverse.GeneratedSources.Single().SourceText.ToString());
    }

    [TestMethod]
    [FourBackends]
    public void Project_without_entries_emits_nothing(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            namespace Demo;
            public static class HostCode
            {
                public static uint Value => 17u;
            }
            """;

        WarpCLRGeneratorTestResult result = WarpCLRGeneratorTestHarness.Run(
            $"GeneratorEmpty{backend}",
            source);
        Assert.IsEmpty(result.DriverDiagnostics);
        Assert.IsEmpty(RoslynCompilationFactory.GetCompilerErrors(result.OutputCompilation));
        Assert.IsEmpty(result.GeneratedSources);
        Assert.IsNull(result.GetManifest());
    }

    [TestMethod]
    [FourBackends]
    public void Unicode_identity_uses_canonical_json(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            namespace München;
            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Δ([WarpInput] uint value) => value;
            }
            """;

        WarpCLRGeneratorTestResult result = RunValid(
            $"GeneratorUnicode{backend}",
            source);
        string manifest = RequireManifest(result);
        StringAssert.Contains(manifest, "M\\u00FCnchen.Kernels");
        StringAssert.Contains(manifest, "\\u0394");
        AssertCanonicalManifestReachesGraphHash(result);
    }

    [TestMethod]
    [FourBackends]
    public void Invalid_role_is_not_hidden_from_verifier(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            namespace Demo;
            public static class InvalidKernels
            {
                [WarpEntryPoint]
                public static uint Transform(uint value) => value;
            }
            """;

        WarpCLRGeneratorTestResult result = RunValid(
            $"GeneratorInvalid{backend}",
            source);
        string manifest = RequireManifest(result);
        StringAssert.Contains(manifest, "\"parameterRoles\":[\"invalid\"]");
        byte[] assembly = Emit(result.OutputCompilation);
        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));
        Assert.AreEqual("WRPCIL2001", exception.Code);
    }

    private static WarpCLRGeneratorTestResult RunValid(
        string assemblyName,
        params string[] sources)
    {
        WarpCLRGeneratorTestResult result = WarpCLRGeneratorTestHarness.Run(
            assemblyName,
            sources);
        Assert.IsEmpty(result.DriverDiagnostics, Describe(result.DriverDiagnostics));
        Diagnostic[] errors = RoslynCompilationFactory.GetCompilerErrors(
            result.OutputCompilation);
        Assert.IsEmpty(errors, Describe(errors));
        Assert.HasCount(1, result.GeneratedSources);
        return result;
    }

    private static string RequireManifest(WarpCLRGeneratorTestResult result)
    {
        string? manifest = result.GetManifest();
        Assert.IsNotNull(manifest);
        return manifest;
    }

    private static void AssertCanonicalManifestReachesGraphHash(
        WarpCLRGeneratorTestResult result)
    {
        byte[] assembly = Emit(result.OutputCompilation);
        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));
        Assert.AreEqual("WRPCIL2004", exception.Code);
    }

    private static byte[] Emit(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        EmitResult emit = compilation.Emit(stream);
        Assert.IsTrue(emit.Success, Describe(emit.Diagnostics));
        return stream.ToArray();
    }

    private static void AssertUppercaseHash(string? value)
    {
        Assert.IsNotNull(value);
        Assert.HasCount(64, value);
        Assert.IsTrue(value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F'));
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(value => value.ToString()));

    private static void AssertBackend(WarpBackendKind backend) =>
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
}
