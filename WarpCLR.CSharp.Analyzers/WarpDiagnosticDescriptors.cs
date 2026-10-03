using Microsoft.CodeAnalysis;

namespace WarpCLR.CSharp.Analyzers;

internal static class WarpDiagnosticDescriptors
{
    private const string Category = "WarpCLR.CSharp";

    public static readonly DiagnosticDescriptor EntryDeclaration = new(
        "WCS1001",
        "Invalid Warp entry declaration",
        "Entry point '{0}' is outside the WarpCLR 0.1.0 declaration profile",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ParameterRoles = new(
        "WCS1002",
        "Invalid Warp parameter roles",
        "Entry point '{0}' must declare one or more inputs before all scalar parameters",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedOperation = new(
        "WCS1003",
        "Unsupported Warp operation",
        "Operation '{0}' is outside the WarpCLR 0.1.0 entry profile",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EntryAllocation = new(
        "WCS1004",
        "Unsupported Warp entry allocation",
        "Managed allocation is not available inside a WarpCLR 0.1.0 entry point",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ImplicitRuntimeBehavior = new(
        "WCS1005",
        "Unsupported implicit CLR behavior",
        "Method '{0}' requires {1}, which is outside the WarpCLR 0.1.0 entry profile",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SourceResourceLimit = new(
        "WCS1006",
        "Warp source compilation resource limit exceeded",
        "The closed helper graph requests {0} functions, exceeding the portable source compilation limit of {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ModuleInitialization = new(
        "WCS1007",
        "Unsupported Warp module initialization",
        "Module initializer '{0}' cannot be present in a module containing WarpCLR entry points",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

}
