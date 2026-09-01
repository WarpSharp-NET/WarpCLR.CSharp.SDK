using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;
using WarpCLR.Verifier;

namespace WarpCLR.CSharp.Build.Tests;

[TestClass]
public sealed class WarpCLRAssemblyFinalizerTests
{
    private const string ValidMapSource = """
        using WarpCLR.CSharp;

        namespace Demo;

        public static class Kernels
        {
            [WarpEntryPoint]
            public static uint Transform(
                [WarpInput] uint value,
                [WarpScalar] uint scalar) => (value * 33u) + scalar;
        }
        """;

    [TestMethod]
    [FourBackends]
    public void Finalized_map_executes_on_selected_backend(WarpBackendKind backend)
    {
        AssertBackend(backend);
        byte[] original = GenerateAssembly(
            $"FinalizedMap{backend}",
            ValidMapSource);
        WarpVerificationException initial = Assert.ThrowsExactly<WarpVerificationException>(
            () => new WarpModuleVerifier().Verify(original));
        Assert.AreEqual("WRPCIL2004", initial.Code);

        WarpCLRAssemblyFinalization finalized = WarpCLRAssemblyFinalizer
            .FinalizeAssembly(original);
        Assert.IsTrue(finalized.HasManifest);
        Assert.IsTrue(finalized.Changed);
        Assert.IsNotNull(finalized.Module);
        Assert.HasCount(1, finalized.Module.Entries);
        Assert.AreEqual(
            "Demo.Kernels.Transform",
            finalized.Module.Entries[0].Identity);

        WarpAotPackage package = WarpCLRCompiler.CompilePackage(
            finalized.AssemblyBytes);
        string packageDirectory = CreateTemporaryDirectory();
        try
        {
            package.WriteToDirectory(packageDirectory);
            WarpCLRProgram program = WarpCLRProgram.Load(
                finalized.AssemblyBytes,
                packageDirectory);
            WarpCLRSession session = program.CreateDevelopmentSession(backend);
            Assert.AreEqual(backend, session.Backend);
            Assert.AreEqual(
                WarpDevelopmentExecutionMode.SemanticEmulation,
                session.Mode);

            WarpUInt32Buffer input = WarpUInt32Buffer.From(
                0u,
                1u,
                uint.MaxValue);
            WarpUInt32Buffer output = session.Dispatch(
                new WarpMapEntry("Demo.Kernels.Transform", 1, 1),
                [input],
                [7u]);
            CollectionAssert.AreEqual(
                input.Select(value => unchecked((value * 33u) + 7u)).ToArray(),
                output.ToArray());
        }
        finally
        {
            DeleteTemporaryDirectory(packageDirectory);
        }
    }

    [TestMethod]
    [FourBackends]
    public void Finalization_is_deterministic_and_idempotent(WarpBackendKind backend)
    {
        AssertBackend(backend);
        byte[] original = GenerateAssembly(
            $"FinalizationDeterminism{backend}",
            ValidMapSource);
        byte[] originalCopy = original.ToArray();

        WarpCLRAssemblyFinalization first = WarpCLRAssemblyFinalizer
            .FinalizeAssembly(original);
        WarpCLRAssemblyFinalization second = WarpCLRAssemblyFinalizer
            .FinalizeAssembly(original);
        WarpCLRAssemblyFinalization repeated = WarpCLRAssemblyFinalizer
            .FinalizeAssembly(first.AssemblyBytes);

        CollectionAssert.AreEqual(originalCopy, original);
        CollectionAssert.AreEqual(first.AssemblyBytes, second.AssemblyBytes);
        CollectionAssert.AreEqual(first.AssemblyBytes, repeated.AssemblyBytes);
        Assert.IsTrue(first.Changed);
        Assert.IsTrue(second.Changed);
        Assert.IsFalse(repeated.Changed);
        Assert.AreEqual(
            first.Module!.AssemblyHash,
            repeated.Module!.AssemblyHash);
    }

    [TestMethod]
    [FourBackends]
    public void Multiple_entries_are_finalized_and_execute(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;

            namespace Demo;

            public static class AlphaKernels
            {
                [WarpEntryPoint]
                public static uint Add(
                    [WarpInput] uint value,
                    [WarpScalar] uint scalar) => value + scalar;
            }

            public static class OmegaKernels
            {
                [WarpEntryPoint]
                public static uint Xor(
                    [WarpInput] uint value,
                    [WarpScalar] uint scalar) => value ^ scalar;
            }
            """;
        byte[] original = GenerateAssembly(
            $"MultipleEntries{backend}",
            source);

        WarpCLRAssemblyFinalization finalized = WarpCLRAssemblyFinalizer
            .FinalizeAssembly(original);

        Assert.IsTrue(finalized.Changed);
        Assert.IsNotNull(finalized.Module);
        CollectionAssert.AreEqual(
            new[]
            {
                "Demo.AlphaKernels.Add",
                "Demo.OmegaKernels.Xor",
            },
            finalized.Module.Entries.Select(entry => entry.Identity).ToArray());

        WarpAotPackage package = WarpCLRCompiler.CompilePackage(
            finalized.AssemblyBytes);
        string packageDirectory = CreateTemporaryDirectory();
        try
        {
            package.WriteToDirectory(packageDirectory);
            WarpCLRProgram program = WarpCLRProgram.Load(
                finalized.AssemblyBytes,
                packageDirectory);
            WarpCLRSession session = program.CreateDevelopmentSession(backend);
            WarpUInt32Buffer input = WarpUInt32Buffer.From(0u, 1u, uint.MaxValue);

            WarpUInt32Buffer added = session.Dispatch(
                new WarpMapEntry("Demo.AlphaKernels.Add", 1, 1),
                [input],
                [7u]);
            WarpUInt32Buffer xored = session.Dispatch(
                new WarpMapEntry("Demo.OmegaKernels.Xor", 1, 1),
                [input],
                [7u]);

            CollectionAssert.AreEqual(
                input.Select(value => unchecked(value + 7u)).ToArray(),
                added.ToArray());
            CollectionAssert.AreEqual(
                input.Select(value => value ^ 7u).ToArray(),
                xored.ToArray());
        }
        finally
        {
            DeleteTemporaryDirectory(packageDirectory);
        }
    }

    [TestMethod]
    [FourBackends]
    public void Assembly_without_entries_is_not_changed(WarpBackendKind backend)
    {
        AssertBackend(backend);
        byte[] original = GenerateAssembly(
            $"NoEntries{backend}",
            "namespace Demo; public static class HostCode { public static uint Value => 17u; }");

        WarpCLRAssemblyFinalization result = WarpCLRAssemblyFinalizer
            .FinalizeAssembly(original);

        Assert.IsFalse(result.HasManifest);
        Assert.IsFalse(result.Changed);
        Assert.IsNull(result.Module);
        CollectionAssert.AreEqual(original, result.AssemblyBytes);
    }

    [TestMethod]
    [FourBackends]
    public void Unknown_graph_hash_is_not_repaired(WarpBackendKind backend)
    {
        AssertBackend(backend);
        WarpCLRGeneratorTestResult generated = Generate(
            $"UnknownHash{backend}",
            ValidMapSource);
        byte[] original = Emit(generated.OutputCompilation);
        string placeholder = ReadFirstGraphHash(generated);
        string stale = new('F', 64);
        Assert.AreNotEqual(placeholder, stale);
        byte[] tampered = ReplaceUnique(original, placeholder, stale);
        byte[] tamperedCopy = tampered.ToArray();

        WarpCLRBuildException exception = Assert.ThrowsExactly<WarpCLRBuildException>(
            () => WarpCLRAssemblyFinalizer.FinalizeAssembly(tampered));

        Assert.AreEqual("WCSB1003", exception.Code);
        CollectionAssert.AreEqual(tamperedCopy, tampered);
    }

    [TestMethod]
    [FourBackends]
    public void Strong_name_marker_is_rejected(WarpBackendKind backend)
    {
        AssertBackend(backend);
        byte[] original = GenerateAssembly(
            $"StrongName{backend}",
            ValidMapSource);
        byte[] marked = MarkStrongNameSigned(original);
        byte[] markedCopy = marked.ToArray();

        WarpCLRBuildException exception = Assert.ThrowsExactly<WarpCLRBuildException>(
            () => WarpCLRAssemblyFinalizer.FinalizeAssembly(marked));

        Assert.AreEqual("WCSB1002", exception.Code);
        CollectionAssert.AreEqual(markedCopy, marked);
    }

    [TestMethod]
    [FourBackends]
    public void Invalid_CIL_is_rejected_after_hash_finalization(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            namespace Demo;
            public static class InvalidKernels
            {
                [WarpEntryPoint]
                public static uint Divide(
                    [WarpInput] uint value,
                    [WarpScalar] uint divisor) => value / divisor;
            }
            """;
        byte[] original = GenerateAssembly(
            $"InvalidCil{backend}",
            source);
        byte[] originalCopy = original.ToArray();

        WarpVerificationException exception = Assert.ThrowsExactly<WarpVerificationException>(
            () => WarpCLRAssemblyFinalizer.FinalizeAssembly(original));

        Assert.AreEqual("WRPCIL1001", exception.Code);
        CollectionAssert.AreEqual(originalCopy, original);
    }

    [TestMethod]
    [FourBackends]
    public void Failed_file_finalization_preserves_the_file(WarpBackendKind backend)
    {
        AssertBackend(backend);
        const string source = """
            using WarpCLR.CSharp;
            namespace Demo;
            public static class InvalidKernels
            {
                [WarpEntryPoint]
                public static uint Branch([WarpInput] uint value) =>
                    value == 0u ? 1u : value;
            }
            """;
        byte[] original = GenerateAssembly(
            $"InvalidFile{backend}",
            source);
        string directory = CreateTemporaryDirectory();
        string assemblyPath = Path.Combine(directory, "Invalid.dll");
        try
        {
            File.WriteAllBytes(assemblyPath, original);
            Assert.ThrowsExactly<WarpVerificationException>(
                () => WarpCLRAssemblyFinalizer.FinalizeAssemblyFile(assemblyPath));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(assemblyPath));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static WarpCLRGeneratorTestResult Generate(
        string assemblyName,
        params string[] sources)
    {
        WarpCLRGeneratorTestResult result = WarpCLRGeneratorTestHarness.Run(
            assemblyName,
            sources);
        Assert.IsEmpty(result.DriverDiagnostics, Describe(result.DriverDiagnostics));
        Assert.IsEmpty(
            RoslynCompilationFactory.GetCompilerErrors(result.OutputCompilation));
        return result;
    }

    private static byte[] GenerateAssembly(
        string assemblyName,
        params string[] sources) => Emit(
            Generate(assemblyName, sources).OutputCompilation);

    private static byte[] Emit(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        EmitResult result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, Describe(result.Diagnostics));
        return stream.ToArray();
    }

    private static string ReadFirstGraphHash(
        WarpCLRGeneratorTestResult result)
    {
        string? manifest = result.GetManifest();
        Assert.IsNotNull(manifest);
        using JsonDocument document = JsonDocument.Parse(manifest);
        return document.RootElement
            .GetProperty("entries")[0]
            .GetProperty("graphHash")
            .GetString()!;
    }

    private static byte[] ReplaceUnique(
        byte[] source,
        string current,
        string replacement)
    {
        byte[] currentBytes = Encoding.UTF8.GetBytes(current);
        byte[] replacementBytes = Encoding.UTF8.GetBytes(replacement);
        Assert.HasCount(currentBytes.Length, replacementBytes);
        int offset = source.AsSpan().IndexOf(currentBytes);
        Assert.IsGreaterThanOrEqualTo(0, offset);
        Assert.AreEqual(
            -1,
            source.AsSpan(offset + currentBytes.Length).IndexOf(currentBytes));

        byte[] result = source.ToArray();
        replacementBytes.CopyTo(result.AsSpan(offset));
        return result;
    }

    private static byte[] MarkStrongNameSigned(byte[] source)
    {
        byte[] result = source.ToArray();
        using var stream = new MemoryStream(result, writable: false);
        using var peReader = new PEReader(stream);
        int corHeaderOffset = peReader.PEHeaders.CorHeaderStartOffset;
        Assert.IsGreaterThanOrEqualTo(0, corHeaderOffset);

        Span<byte> flagBytes = result.AsSpan(corHeaderOffset + 16, sizeof(uint));
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(flagBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(
            flagBytes,
            flags | (uint)CorFlags.StrongNameSigned);
        return result;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "WarpCLR.CSharp.Build.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(value => value.ToString()));

    private static void AssertBackend(WarpBackendKind backend) =>
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));
}
