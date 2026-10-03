using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace WarpCLR.CSharp.Testing;

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
        AttributeData[] matches = OutputCompilation.Assembly
            .GetAttributes()
            .Where(attribute =>
                string.Equals(attribute.AttributeClass?.ToDisplayString(),
                    "System.Reflection.AssemblyMetadataAttribute", StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 2 &&
                Equals(attribute.ConstructorArguments[0].Value, "WarpCIL.Manifest"))
            .ToArray();
        Assert.IsLessThanOrEqualTo(1, matches.Length, "The generated manifest must be unique.");
        return matches.Length == 0 ? null : matches[0].ConstructorArguments[1].Value as string;
    }
}
