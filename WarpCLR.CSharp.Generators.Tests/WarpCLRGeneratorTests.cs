using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using WarpCLR.IR;
using WarpCLR.Verifier;

namespace WarpCLR.CSharp.Generators.Tests;

[TestClass]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest creates this internal fixture through reflected discovery.")]
internal sealed class WarpCLRGeneratorTests
{
    private static readonly string[] ExpectedMapRoles = ["input", "input", "scalar"];
    private static readonly string[] ExpectedModeMethods = ["Map", "Maximum", "Minimum", "WrappingSum"];
    private static readonly string[] ExpectedModes = ["map", "reduce-maximum", "reduce-minimum", "reduce-wrapping-sum"];
    [TestMethod]
    [FourBackends]
    public void MapEntryEmitsManifestAndCatalog(WarpBackendKind backend)
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
        Assert.AreEqual($"GeneratorMap{backend}", root.GetProperty("producer").GetString(), StringComparer.Ordinal);
        Assert.AreEqual("2.3.4.0", root.GetProperty("producerVersion").GetString(), StringComparer.Ordinal);

        JsonElement entry = root.GetProperty("entries")[0];
        Assert.AreEqual("Demo.Kernels", entry.GetProperty("type").GetString(), StringComparer.Ordinal);
        Assert.AreEqual("Transform", entry.GetProperty("method").GetString(), StringComparer.Ordinal);
        Assert.AreEqual("map", entry.GetProperty("execution").GetString(), StringComparer.Ordinal);
        CollectionAssert.AreEqual(
            ExpectedMapRoles,
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
            .SingleItem();
        Assert.AreEqual(
            "WarpCLR.CSharp.WarpMapEntry",
            property.Type.ToDisplayString(), StringComparer.Ordinal);
        StringAssert.Contains(
            result.GeneratedSources.SingleItem().SourceText.ToString(),
            "new global::WarpCLR.CSharp.WarpMapEntry(\"Demo.Kernels.Transform\", 2, 1)", StringComparison.Ordinal);
        AssertCanonicalManifestReachesGraphHash(result);
    }

    [TestMethod]
    [FourBackends]
    public void AllExecutionModesEmitExactNames(WarpBackendKind backend)
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
            ExpectedModeMethods,
            entries.EnumerateArray()
                .Select(entry => entry.GetProperty("method").GetString())
                .ToArray());
        CollectionAssert.AreEqual(
            ExpectedModes,
            entries.EnumerateArray()
                .Select(entry => entry.GetProperty("execution").GetString())
                .ToArray());

        INamedTypeSymbol catalog = result.OutputCompilation
            .GetTypeByMetadataName("Demo.WarpCLRReductionsEntries")!;
        Assert.AreEqual(
            "WarpCLR.CSharp.WarpMapEntry",
            catalog.GetMembers("Map").OfType<IPropertySymbol>().SingleItem().Type.ToDisplayString(), StringComparer.Ordinal);
        foreach (string name in new[] { "Maximum", "Minimum", "WrappingSum" })
        {
            Assert.AreEqual(
                "WarpCLR.CSharp.WarpReductionEntry",
                catalog.GetMembers(name).OfType<IPropertySymbol>().SingleItem().Type.ToDisplayString(), StringComparer.Ordinal);
        }

        AssertCanonicalManifestReachesGraphHash(result);
    }

    [TestMethod]
    [FourBackends]
    public void EntryOrderIsSourceOrderIndependent(WarpBackendKind backend)
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

        Assert.AreEqual(RequireManifest(forward), RequireManifest(reverse), StringComparer.Ordinal);
        Assert.AreEqual(
            forward.GeneratedSources.SingleItem().SourceText.ToString(),
            reverse.GeneratedSources.SingleItem().SourceText.ToString(), StringComparer.Ordinal);
    }

    [TestMethod]
    [FourBackends]
    public void ProjectWithoutEntriesEmitsNothing(WarpBackendKind backend)
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
    public void UnicodeIdentityUsesCanonicalJson(WarpBackendKind backend)
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
        StringAssert.Contains(manifest, "M\\u00FCnchen.Kernels", StringComparison.Ordinal);
        StringAssert.Contains(manifest, "\\u0394", StringComparison.Ordinal);
        AssertCanonicalManifestReachesGraphHash(result);
    }

    [TestMethod]
    [FourBackends]
    public void InvalidRoleIsNotHiddenFromVerifier(WarpBackendKind backend)
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
        StringAssert.Contains(manifest, "\"parameterRoles\":[\"invalid\"]", StringComparison.Ordinal);
        byte[] assembly = Emit(result.OutputCompilation);
        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(assembly));
        Assert.AreEqual("WRPCIL2001", exception.Code, StringComparer.Ordinal);
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
        Assert.AreEqual("WRPCIL2004", exception.Code, StringComparer.Ordinal);
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
