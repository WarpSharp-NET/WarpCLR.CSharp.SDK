using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace WarpCLR.CSharp.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WarpCLRAnalyzer : DiagnosticAnalyzer
{
    private const string EntryAttributeName =
        "WarpCLR.CSharp.WarpEntryPointAttribute";
    private const string InputAttributeName =
        "WarpCLR.CSharp.WarpInputAttribute";
    private const string ScalarAttributeName =
        "WarpCLR.CSharp.WarpScalarAttribute";
    private const string WarpTypeName = "WarpCLR.CSharp.WarpCLRMemory";
    private const string ScopeTypeName = "WarpCLR.CSharp.WarpScope";
    private const string ScopedObjectTypeName =
        "WarpCLR.CSharp.WarpScopedObject";
    private const string ScopedArrayTypeName =
        "WarpCLR.CSharp.WarpScopedUInt32Array";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            WarpDiagnosticDescriptors.EntryDeclaration,
            WarpDiagnosticDescriptors.ParameterRoles,
            WarpDiagnosticDescriptors.UnsupportedOperation,
            WarpDiagnosticDescriptors.EntryAllocation,
            WarpDiagnosticDescriptors.ScopeRequiresUsing,
            WarpDiagnosticDescriptors.ScopedValueEscape);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(
        CompilationStartAnalysisContext context)
    {
        INamedTypeSymbol? entryAttribute = context.Compilation
            .GetTypeByMetadataName(EntryAttributeName);
        INamedTypeSymbol? inputAttribute = context.Compilation
            .GetTypeByMetadataName(InputAttributeName);
        INamedTypeSymbol? scalarAttribute = context.Compilation
            .GetTypeByMetadataName(ScalarAttributeName);

        if (entryAttribute is not null &&
            inputAttribute is not null &&
            scalarAttribute is not null)
        {
            context.RegisterSymbolAction(
                analysisContext => AnalyzeMethod(
                    analysisContext,
                    entryAttribute,
                    inputAttribute,
                    scalarAttribute),
                SymbolKind.Method);
            context.RegisterOperationBlockAction(
                analysisContext => AnalyzeOperationBlock(
                    analysisContext,
                    entryAttribute));
        }

        IMethodSymbol? scopeMethod = context.Compilation
            .GetTypeByMetadataName(WarpTypeName)?
            .GetMembers("Scope")
            .OfType<IMethodSymbol>()
            .SingleOrDefault();
        if (scopeMethod is not null)
        {
            context.RegisterOperationAction(
                analysisContext => AnalyzeScopeInvocation(
                    analysisContext,
                    scopeMethod),
                OperationKind.Invocation);
        }

        INamedTypeSymbol? scopeType = context.Compilation
            .GetTypeByMetadataName(ScopeTypeName);
        INamedTypeSymbol? scopedObjectType = context.Compilation
            .GetTypeByMetadataName(ScopedObjectTypeName);
        INamedTypeSymbol? scopedArrayType = context.Compilation
            .GetTypeByMetadataName(ScopedArrayTypeName);
        if (scopeType is not null &&
            scopedObjectType is not null &&
            scopedArrayType is not null)
        {
            context.RegisterOperationAction(
                analysisContext => AnalyzeReturn(
                    analysisContext,
                    scopeType,
                    scopedObjectType,
                    scopedArrayType),
                OperationKind.Return);
        }
    }

    private static void AnalyzeMethod(
        SymbolAnalysisContext context,
        INamedTypeSymbol entryAttribute,
        INamedTypeSymbol inputAttribute,
        INamedTypeSymbol scalarAttribute)
    {
        var method = (IMethodSymbol)context.Symbol;
        AttributeData? entryData = GetAttribute(method, entryAttribute);
        if (entryData is null)
        {
            return;
        }

        if (!HasValidDeclaration(method, entryData))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    WarpDiagnosticDescriptors.EntryDeclaration,
                    GetLocation(method),
                    method.ToDisplayString()));
        }

        if (!HasValidParameterRoles(
                method,
                inputAttribute,
                scalarAttribute))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    WarpDiagnosticDescriptors.ParameterRoles,
                    GetLocation(method),
                    method.ToDisplayString()));
        }
    }

    private static bool HasValidDeclaration(
        IMethodSymbol method,
        AttributeData entryData)
    {
        bool hasBody = method.DeclaringSyntaxReferences.Any(
            reference => reference.GetSyntax() is MethodDeclarationSyntax declaration &&
                (declaration.Body is not null || declaration.ExpressionBody is not null));
        int overloadCount = method.ContainingType
            .GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Count(candidate => candidate.MethodKind == MethodKind.Ordinary);
        bool validExecution = entryData.ConstructorArguments.Length == 1 &&
            entryData.ConstructorArguments[0].Value is int execution &&
            execution is >= 0 and <= 3;

        return method.MethodKind == MethodKind.Ordinary &&
            method.IsStatic &&
            !method.IsAbstract &&
            !method.IsExtern &&
            !method.IsVararg &&
            !method.IsGenericMethod &&
            method.ReturnType.SpecialType == SpecialType.System_UInt32 &&
            method.Parameters.Length > 0 &&
            method.Parameters.All(
                parameter => parameter.RefKind == RefKind.None &&
                    parameter.Type.SpecialType == SpecialType.System_UInt32) &&
            method.ContainingType.ContainingType is null &&
            !method.ContainingType.IsGenericType &&
            overloadCount == 1 &&
            hasBody &&
            validExecution;
    }

    private static bool HasValidParameterRoles(
        IMethodSymbol method,
        INamedTypeSymbol inputAttribute,
        INamedTypeSymbol scalarAttribute)
    {
        bool foundInput = false;
        bool foundScalar = false;
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            bool isInput = GetAttribute(parameter, inputAttribute) is not null;
            bool isScalar = GetAttribute(parameter, scalarAttribute) is not null;
            if (isInput == isScalar)
            {
                return false;
            }

            if (isInput)
            {
                if (foundScalar)
                {
                    return false;
                }

                foundInput = true;
            }
            else
            {
                foundScalar = true;
            }
        }

        return foundInput;
    }

    private static void AnalyzeOperationBlock(
        OperationBlockAnalysisContext context,
        INamedTypeSymbol entryAttribute)
    {
        if (context.OwningSymbol is not IMethodSymbol method ||
            GetAttribute(method, entryAttribute) is null)
        {
            return;
        }

        var walker = new ProfileOperationWalker(context.ReportDiagnostic);
        foreach (IOperation operation in context.OperationBlocks)
        {
            if (operation is IAttributeOperation)
            {
                continue;
            }

            walker.Visit(operation);
        }
    }

    private static void AnalyzeScopeInvocation(
        OperationAnalysisContext context,
        IMethodSymbol scopeMethod)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!SymbolEqualityComparer.Default.Equals(
                invocation.TargetMethod.OriginalDefinition,
                scopeMethod.OriginalDefinition) ||
            IsUsingResource(invocation.Syntax))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                WarpDiagnosticDescriptors.ScopeRequiresUsing,
                invocation.Syntax.GetLocation()));
    }

    private static bool IsUsingResource(SyntaxNode syntax)
    {
        foreach (SyntaxNode ancestor in syntax.AncestorsAndSelf())
        {
            if (ancestor is LocalDeclarationStatementSyntax declaration &&
                declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
            {
                return declaration.Declaration.Span.Contains(syntax.Span);
            }

            if (ancestor is UsingStatementSyntax usingStatement)
            {
                bool inDeclaration = usingStatement.Declaration?.Span
                    .Contains(syntax.Span) == true;
                bool inExpression = usingStatement.Expression?.Span
                    .Contains(syntax.Span) == true;
                if (inDeclaration || inExpression)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AnalyzeReturn(
        OperationAnalysisContext context,
        INamedTypeSymbol scopeType,
        INamedTypeSymbol scopedObjectType,
        INamedTypeSymbol scopedArrayType)
    {
        var returnOperation = (IReturnOperation)context.Operation;
        ITypeSymbol? type = returnOperation.ReturnedValue?.Type;
        if (type is null ||
            (!SymbolEqualityComparer.Default.Equals(type, scopeType) &&
             !SymbolEqualityComparer.Default.Equals(type, scopedObjectType) &&
             !SymbolEqualityComparer.Default.Equals(type, scopedArrayType)))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                WarpDiagnosticDescriptors.ScopedValueEscape,
                returnOperation.Syntax.GetLocation()));
    }

    private static AttributeData? GetAttribute(
        ISymbol symbol,
        INamedTypeSymbol attributeType) => symbol
            .GetAttributes()
            .FirstOrDefault(
                attribute => SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass,
                    attributeType));

    private static Location GetLocation(ISymbol symbol) => symbol.Locations
        .First(location => location.IsInSource);

    private sealed class ProfileOperationWalker : OperationWalker
    {
        private readonly Action<Diagnostic> reportDiagnostic;

        public ProfileOperationWalker(Action<Diagnostic> reportDiagnostic)
        {
            this.reportDiagnostic = reportDiagnostic;
        }

        public override void Visit(IOperation? operation)
        {
            if (operation is null)
            {
                return;
            }

            if (IsAllocation(operation))
            {
                reportDiagnostic(
                    Diagnostic.Create(
                        WarpDiagnosticDescriptors.EntryAllocation,
                        operation.Syntax.GetLocation()));
                return;
            }

            if (operation is IVariableDeclaratorOperation declarator &&
                !IsUInt32(declarator.Symbol.Type))
            {
                reportDiagnostic(
                    Diagnostic.Create(
                        WarpDiagnosticDescriptors.UnsupportedOperation,
                        operation.Syntax.GetLocation(),
                        operation.Kind.ToString()));
                base.Visit(operation);
                return;
            }

            if (!IsSupported(operation))
            {
                reportDiagnostic(
                    Diagnostic.Create(
                        WarpDiagnosticDescriptors.UnsupportedOperation,
                        operation.Syntax.GetLocation(),
                        operation.Kind.ToString()));
                return;
            }

            base.Visit(operation);
        }

        private static bool IsAllocation(IOperation operation) => operation is
            IObjectCreationOperation or
            IArrayCreationOperation or
            IAnonymousObjectCreationOperation or
            IDelegateCreationOperation or
            IDynamicObjectCreationOperation;

        private static bool IsSupported(IOperation operation) => operation switch
        {
            IMethodBodyOperation => true,
            IBlockOperation => true,
            IReturnOperation => true,
            IVariableDeclarationGroupOperation => true,
            IVariableDeclarationOperation => true,
            IVariableDeclaratorOperation => true,
            IVariableInitializerOperation => true,
            IExpressionStatementOperation => true,
            ISimpleAssignmentOperation assignment =>
                assignment.Target is ILocalReferenceOperation,
            IParameterReferenceOperation parameter =>
                IsUInt32(parameter.Parameter.Type),
            ILocalReferenceOperation local => IsUInt32(local.Local.Type),
            ILiteralOperation literal => IsInteger(literal.Type),
            IDefaultValueOperation value => IsUInt32(value.Type),
            IFieldReferenceOperation field =>
                field.Instance is null &&
                field.Field.HasConstantValue &&
                IsInteger(field.Type),
            IParenthesizedOperation => true,
            IConversionOperation conversion =>
                IsSupportedConversion(conversion),
            IUnaryOperation unary => IsSupportedUnary(unary),
            IBinaryOperation binary => IsSupportedBinary(binary),
            ICompoundAssignmentOperation assignment =>
                assignment.Target is ILocalReferenceOperation &&
                !assignment.IsChecked &&
                IsSupportedBinaryOperator(assignment.OperatorKind),
            IEmptyOperation => true,
            _ => false,
        };

        private static bool IsSupportedConversion(
            IConversionOperation conversion) =>
            !conversion.IsChecked &&
            conversion.OperatorMethod is null &&
            IsInteger(conversion.Operand.Type) &&
            IsInteger(conversion.Type);

        private static bool IsSupportedUnary(IUnaryOperation unary) =>
            !unary.IsChecked &&
            unary.OperatorMethod is null &&
            unary.OperatorKind == UnaryOperatorKind.BitwiseNegation &&
            IsUInt32(unary.Operand.Type) &&
            IsUInt32(unary.Type);

        private static bool IsSupportedBinary(IBinaryOperation binary)
        {
            if (binary.IsChecked ||
                binary.IsLifted ||
                binary.OperatorMethod is not null ||
                !IsSupportedBinaryOperator(binary.OperatorKind) ||
                !IsUInt32(binary.LeftOperand.Type) ||
                !IsUInt32(binary.Type))
            {
                return false;
            }

            bool isShift = binary.OperatorKind is
                BinaryOperatorKind.LeftShift or
                BinaryOperatorKind.RightShift or
                BinaryOperatorKind.UnsignedRightShift;
            return isShift
                ? IsInteger(binary.RightOperand.Type)
                : IsUInt32(binary.RightOperand.Type);
        }

        private static bool IsSupportedBinaryOperator(
            BinaryOperatorKind operatorKind) => operatorKind is
                BinaryOperatorKind.Add or
                BinaryOperatorKind.Subtract or
                BinaryOperatorKind.Multiply or
                BinaryOperatorKind.And or
                BinaryOperatorKind.Or or
                BinaryOperatorKind.ExclusiveOr or
                BinaryOperatorKind.LeftShift or
                BinaryOperatorKind.RightShift or
                BinaryOperatorKind.UnsignedRightShift;

        private static bool IsInteger(ITypeSymbol? type) => type?.SpecialType is
            SpecialType.System_Int32 or SpecialType.System_UInt32;

        private static bool IsUInt32(ITypeSymbol? type) =>
            type?.SpecialType == SpecialType.System_UInt32;
    }
}
