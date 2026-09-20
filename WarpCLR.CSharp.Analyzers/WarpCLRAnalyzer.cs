using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            WarpDiagnosticDescriptors.EntryDeclaration,
            WarpDiagnosticDescriptors.ParameterRoles,
            WarpDiagnosticDescriptors.UnsupportedOperation,
            WarpDiagnosticDescriptors.EntryAllocation);

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

        var walker = new ProfileOperationWalker(
            context.Compilation,
            method,
            context.ReportDiagnostic);
        foreach (IOperation operation in context.OperationBlocks)
        {
            if (operation is IAttributeOperation)
            {
                continue;
            }

            walker.Visit(operation);
        }
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
        private readonly Compilation compilation;
        private readonly Action<Diagnostic> reportDiagnostic;
        private readonly HashSet<IMethodSymbol> visiting = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> visited = new(SymbolEqualityComparer.Default);

        public ProfileOperationWalker(
            Compilation compilation,
            IMethodSymbol entry,
            Action<Diagnostic> reportDiagnostic)
        {
            this.compilation = compilation;
            this.reportDiagnostic = reportDiagnostic;
            visiting.Add(entry);
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

        public override void VisitInvocation(IInvocationOperation operation)
        {
            base.VisitInvocation(operation);

            IMethodSymbol target = operation.TargetMethod;
            if (visited.Contains(target) || !visiting.Add(target))
            {
                return;
            }

            try
            {
                SyntaxReference declaration = target.DeclaringSyntaxReferences.Single();
                SyntaxNode syntax = declaration.GetSyntax();
                SemanticModel model = compilation.GetSemanticModel(syntax.SyntaxTree);
                IOperation? body = syntax switch
                {
                    MethodDeclarationSyntax method when method.Body is not null =>
                        model.GetOperation(method.Body),
                    MethodDeclarationSyntax method when method.ExpressionBody is not null =>
                        model.GetOperation(method.ExpressionBody.Expression),
                    _ => null,
                };
                Visit(body);
                visited.Add(target);
            }
            finally
            {
                visiting.Remove(target);
            }
        }

        private static bool IsAllocation(IOperation operation) => operation is
            IObjectCreationOperation or
            IArrayCreationOperation or
            IAnonymousObjectCreationOperation or
            IDelegateCreationOperation or
            IDynamicObjectCreationOperation;

        private bool IsSupported(IOperation operation) => operation switch
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
            IConditionalOperation conditional =>
                IsSupportedConditional(conditional),
            IInvocationOperation invocation => IsSupportedInvocation(invocation),
            IArgumentOperation => true,
            ICompoundAssignmentOperation assignment =>
                assignment.Target is ILocalReferenceOperation &&
                !assignment.IsChecked &&
                IsSupportedBinaryOperator(assignment.OperatorKind),
            IEmptyOperation => true,
            _ => false,
        };

        private bool IsSupportedInvocation(IInvocationOperation invocation)
        {
            IMethodSymbol method = invocation.TargetMethod;
            return invocation.Instance is null &&
                method.MethodKind == MethodKind.Ordinary &&
                method.IsStatic &&
                !method.IsAbstract &&
                !method.IsExtern &&
                !method.IsVararg &&
                !method.IsGenericMethod &&
                method.ReturnType.SpecialType == SpecialType.System_UInt32 &&
                method.Parameters.All(
                    parameter => parameter.RefKind == RefKind.None &&
                        parameter.Type.SpecialType == SpecialType.System_UInt32) &&
                method.ContainingType.ContainingType is null &&
                !method.ContainingType.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(
                    method.ContainingAssembly,
                    compilation.Assembly) &&
                method.DeclaringSyntaxReferences.Length == 1 &&
                !visiting.Contains(method);
        }

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
                !IsUInt32(binary.LeftOperand.Type))
            {
                return false;
            }

            if (IsSupportedComparisonOperator(binary.OperatorKind))
            {
                return IsUInt32(binary.RightOperand.Type) &&
                    binary.Type?.SpecialType == SpecialType.System_Boolean;
            }

            if (!IsSupportedBinaryOperator(binary.OperatorKind) ||
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

        private static bool IsSupportedConditional(
            IConditionalOperation conditional) =>
            conditional.Condition.Type?.SpecialType == SpecialType.System_Boolean &&
            (conditional.Type is null || IsUInt32(conditional.Type));

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

        private static bool IsSupportedComparisonOperator(
            BinaryOperatorKind operatorKind) => operatorKind is
                BinaryOperatorKind.Equals or
                BinaryOperatorKind.NotEquals or
                BinaryOperatorKind.LessThan or
                BinaryOperatorKind.LessThanOrEqual or
                BinaryOperatorKind.GreaterThan or
                BinaryOperatorKind.GreaterThanOrEqual;

        private static bool IsInteger(ITypeSymbol? type) => type?.SpecialType is
            SpecialType.System_Int32 or SpecialType.System_UInt32;

        private static bool IsUInt32(ITypeSymbol? type) =>
            type?.SpecialType == SpecialType.System_UInt32;
    }
}
