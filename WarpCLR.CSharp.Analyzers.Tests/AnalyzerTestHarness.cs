using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using WarpCLR.CSharp.Testing;

namespace WarpCLR.CSharp.Analyzers.Tests;

internal static class AnalyzerTestHarness
{
    public static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Compilation compilation = RoslynCompilationFactory.Create(
            "AnalyzerTestAssembly",
            source);

        Diagnostic[] compilerErrors = RoslynCompilationFactory
            .GetCompilerErrors(compilation);
        if (compilerErrors.Length != 0)
        {
            Assert.Fail(
                "The analyzer test source has compiler errors. " +
                string.Join(Environment.NewLine, compilerErrors.Select(error => error.ToString())));
        }

        var analyzer = new WarpCLRAnalyzer();
        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
        return diagnostics
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
