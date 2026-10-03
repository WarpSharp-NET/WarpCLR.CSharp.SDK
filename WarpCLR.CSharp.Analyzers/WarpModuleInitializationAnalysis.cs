using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace WarpCLR.CSharp.Analyzers;

internal static class WarpModuleInitializationAnalysis
{
    private const string ModuleInitializerAttributeName = "System.Runtime.CompilerServices.ModuleInitializerAttribute";

    public static void Report(CompilationAnalysisContext context, INamedTypeSymbol entryAttribute)
    {
        var pending = new Stack<INamespaceOrTypeSymbol>();
        var initializers = new List<IMethodSymbol>();
        bool hasEntry = false;
        pending.Push(context.Compilation.Assembly.GlobalNamespace);
        while (pending.Count != 0)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            foreach (ISymbol member in pending.Pop().GetMembers())
            {
                if (member is INamespaceOrTypeSymbol container)
                {
                    pending.Push(container);
                }
                else if (member is IMethodSymbol method)
                {
                    foreach (AttributeData attribute in method.GetAttributes())
                    {
                        hasEntry |= SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, entryAttribute);
                        if (string.Equals(attribute.AttributeClass?.ToDisplayString(), ModuleInitializerAttributeName, StringComparison.Ordinal))
                        {
                            initializers.Add(method);
                        }
                    }
                }
            }
        }

        if (!hasEntry)
        {
            return;
        }

        foreach (IMethodSymbol initializer in initializers.OrderBy(method => method.ToDisplayString(), StringComparer.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                WarpDiagnosticDescriptors.ModuleInitialization,
                initializer.Locations.First(location => location.IsInSource), initializer.ToDisplayString()));
        }
    }
}
