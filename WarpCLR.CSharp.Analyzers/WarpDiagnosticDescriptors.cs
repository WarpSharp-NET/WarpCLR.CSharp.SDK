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

    public static readonly DiagnosticDescriptor ScopeRequiresUsing = new(
        "WCS2001",
        "Warp scope requires using",
        "Declare the Warp scope with a using statement or using declaration",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ScopedValueEscape = new(
        "WCS2002",
        "Warp scoped value escapes",
        "A Warp scoped value cannot leave its activation through a return value",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
