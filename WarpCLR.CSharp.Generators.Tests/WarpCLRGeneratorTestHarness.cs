using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WarpCLR.CSharp.Testing;

namespace WarpCLR.CSharp.Generators.Tests;

internal sealed class WarpCLRGeneratorTestResult
{
    public WarpCLRGeneratorTestResult(
        CSharpCompilation outputCompilation,
        ImmutableArray<Diagnostic> driverDiagnostics,
        ImmutableArray<GeneratedSourceResult> generatedSources)
    {
        OutputCompilation = outputCompilation;
        DriverDiagnostics = driverDiagnostics;
        GeneratedSources = generatedSources;
    }

    public CSharpCompilation OutputCompilation { get; }

    public ImmutableArray<Diagnostic> DriverDiagnostics { get; }

    public ImmutableArray<GeneratedSourceResult> GeneratedSources { get; }

    public string? GetManifest()
    {
        AttributeData? manifest = OutputCompilation.Assembly
            .GetAttributes()
            .SingleOrDefault(
                attribute =>
                    attribute.AttributeClass?.ToDisplayString() ==
                        "System.Reflection.AssemblyMetadataAttribute" &&
                    attribute.ConstructorArguments.Length == 2 &&
                    Equals(
                        attribute.ConstructorArguments[0].Value,
                        "WarpCIL.Manifest"));
        return manifest?.ConstructorArguments[1].Value as string;
    }
}

internal static class WarpCLRGeneratorTestHarness
{
    public static WarpCLRGeneratorTestResult Run(
        string assemblyName,
        params string[] sources)
    {
        CSharpCompilation input = RoslynCompilationFactory.Create(
            assemblyName,
            sources);
        var generator = new WarpCLRGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)input.SyntaxTrees.First().Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            input,
            out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics);

        GeneratorDriverRunResult run = driver.GetRunResult();
        return new WarpCLRGeneratorTestResult(
            (CSharpCompilation)output,
            diagnostics,
            run.Results.SelectMany(result => result.GeneratedSources).ToImmutableArray());
    }
}
