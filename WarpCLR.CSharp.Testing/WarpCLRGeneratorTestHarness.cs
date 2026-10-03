using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WarpCLR.CSharp.Generators;

namespace WarpCLR.CSharp.Testing;

internal static class WarpCLRGeneratorTestHarness
{
    public static WarpCLRGeneratorTestResult Run(
        string assemblyName,
        params string[] sources) => Run(
            RoslynCompilationFactory.Create(assemblyName, sources));

    public static WarpCLRGeneratorTestResult Run(CSharpCompilation input)
    {
        ArgumentNullException.ThrowIfNull(input);
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
