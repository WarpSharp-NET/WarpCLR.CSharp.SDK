using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using WarpCLR.IR;
using WarpCLR.Runtime.Device;
using WarpCLR.Runtime.Host;
using WarpCLR.Sdk;

namespace WarpCLR.CSharp.Testing;

internal static class RoslynCompilationFactory
{
    public static CSharpCompilation Create(
        string assemblyName,
        params string[] sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(sources);

        SyntaxTree[] trees = sources
            .Select(
                (source, index) => CSharpSyntaxTree.ParseText(
                    SourceText.From(source, Encoding.UTF8),
                    new CSharpParseOptions(LanguageVersion.CSharp14),
                    $"Source{index}.cs"))
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            CreateReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: false,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true));
    }

    public static Diagnostic[] GetCompilerErrors(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
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
