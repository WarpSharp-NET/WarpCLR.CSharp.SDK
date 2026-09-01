using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using WarpCLR.IR;
using WarpCLR.Runtime.Device;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;

namespace WarpCLR.CSharp.Analyzers.Tests;

internal static class AnalyzerTestHarness
{
    public static ImmutableArray<Diagnostic> Analyze(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp14));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerTestAssembly",
            [tree],
            CreateReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: false,
                nullableContextOptions: NullableContextOptions.Enable));

        Diagnostic[] compilerErrors = compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (compilerErrors.Length != 0)
        {
            Assert.Fail(
                "The analyzer test source has compiler errors. " +
                string.Join(Environment.NewLine, compilerErrors.Select(error => error.ToString())));
        }

        var analyzer = new WarpCLRAnalyzer();
        return compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult()
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static IEnumerable<MetadataReference> CreateReferences()
    {
        string trustedAssemblies = AppContext.GetData(
            "TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException(
                "The trusted platform assembly list is unavailable.");

        string[] projectAssemblies =
        [
            typeof(WarpEntryPointAttribute).Assembly.Location,
            typeof(WarpBackendKind).Assembly.Location,
            typeof(WarpScopedRegion).Assembly.Location,
            typeof(WarpHostException).Assembly.Location,
            typeof(WarpBuildPipeline).Assembly.Location,
        ];

        return trustedAssemblies
            .Split(Path.PathSeparator)
            .Concat(projectAssemblies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
    }
}
