using System.Collections.Immutable;
using System.Reflection;
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
    // Mirrors WarpCompilationAdmission.MaximumFunctionsPerEntry without a net10.0 analyzer dependency.
    private const int MaximumHelpersPerEntry = 256;
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            WarpDiagnosticDescriptors.EntryDeclaration,
            WarpDiagnosticDescriptors.ParameterRoles,
            WarpDiagnosticDescriptors.UnsupportedOperation,
            WarpDiagnosticDescriptors.EntryAllocation,
            WarpDiagnosticDescriptors.ImplicitRuntimeBehavior,
            WarpDiagnosticDescriptors.SourceResourceLimit,
            WarpDiagnosticDescriptors.ModuleInitialization);

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

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
            context.RegisterCompilationEndAction(
                analysisContext => WarpModuleInitializationAnalysis.Report(analysisContext, entryAttribute));
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

        string? implicitBehavior = GetImplicitRuntimeBehavior(method);
        if (implicitBehavior is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                WarpDiagnosticDescriptors.ImplicitRuntimeBehavior,
                GetLocation(method), method.ToDisplayString(), implicitBehavior));
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
            GetAttribute(method, entryAttribute) is null || GetImplicitRuntimeBehavior(method) is not null)
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

    private static string? GetImplicitRuntimeBehavior(IMethodSymbol method)
    {
        if (!method.ContainingType.StaticConstructors.IsEmpty)
        {
            return "static type initialization";
        }

        return method.MethodImplementationFlags.HasFlag(MethodImplAttributes.Synchronized)
            ? "implicit monitor synchronization"
            : null;
    }

    private sealed class ProfileOperationWalker : OperationWalker
    {
        private readonly Compilation compilation;
        private readonly Action<Diagnostic> reportDiagnostic;
        private readonly HashSet<IMethodSymbol> visiting = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> visited = new(SymbolEqualityComparer.Default);
        private int discoveredHelpers;
        private bool resourceLimitExceeded;

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
            if (operation is null || resourceLimitExceeded)
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
                !IsScalarLocal(declarator.Symbol.Type))
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

            if (operation is IInvocationOperation invocation &&
                GetImplicitRuntimeBehavior(invocation.TargetMethod) is { } implicitBehavior)
            {
                reportDiagnostic(Diagnostic.Create(
                    WarpDiagnosticDescriptors.ImplicitRuntimeBehavior,
                    operation.Syntax.GetLocation(), invocation.TargetMethod.ToDisplayString(), implicitBehavior));
                return;
            }

            base.Visit(operation);
        }

        public override void VisitInvocation(IInvocationOperation operation)
        {
            base.VisitInvocation(operation);
            if (resourceLimitExceeded)
            {
                return;
            }

            IMethodSymbol target = operation.TargetMethod;
            if (visited.Contains(target) || !visiting.Add(target))
            {
                return;
            }

            if (discoveredHelpers == MaximumHelpersPerEntry)
            {
                resourceLimitExceeded = true;
                visiting.Remove(target);
                reportDiagnostic(Diagnostic.Create(
                    WarpDiagnosticDescriptors.SourceResourceLimit,
                    operation.Syntax.GetLocation(), discoveredHelpers + 1, MaximumHelpersPerEntry));
                return;
            }

            discoveredHelpers++;
            try
            {
                SyntaxReference declaration = target.DeclaringSyntaxReferences[0];
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
                IsWritableScalar(assignment.Target) && IsScalarLocal(assignment.Type),
            IParameterReferenceOperation parameter =>
                IsUInt32(parameter.Parameter.Type),
            ILocalReferenceOperation local => IsScalarLocal(local.Local.Type),
            ILiteralOperation literal => IsInteger(literal.Type) || IsBoolean(literal.Type),
            IDefaultValueOperation value => IsScalarLocal(value.Type),
            IFieldReferenceOperation field =>
                field.Instance is null &&
                field.Field.HasConstantValue &&
                (IsInteger(field.Type) || IsBoolean(field.Type)),
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
                IsWritableScalar(assignment.Target) && IsScalarLocal(assignment.Type) &&
                !assignment.IsChecked &&
                !assignment.IsLifted && assignment.OperatorMethod is null &&
                IsSupportedBinaryOperator(assignment.OperatorKind),
            IIncrementOrDecrementOperation increment =>
                IsWritableScalar(increment.Target) && IsUInt32(increment.Type) &&
                !increment.IsChecked && !increment.IsLifted && increment.OperatorMethod is null,
            IWhileLoopOperation loop =>
                IsBoolean(loop.Condition?.Type) && !loop.ConditionIsUntil && loop.IgnoredCondition is null,
            IForLoopOperation loop => loop.Condition is null || IsBoolean(loop.Condition.Type),
            IBranchOperation branch => branch.BranchKind is BranchKind.Break or BranchKind.Continue or BranchKind.GoTo,
            ILabeledOperation => true,
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
            ((unary.OperatorKind == UnaryOperatorKind.BitwiseNegation &&
                IsUInt32(unary.Operand.Type) && IsUInt32(unary.Type)) ||
                (unary.OperatorKind == UnaryOperatorKind.Not &&
                    IsBoolean(unary.Operand.Type) && IsBoolean(unary.Type)));

        private static bool IsSupportedBinary(IBinaryOperation binary)
        {
            if (binary.IsChecked ||
                binary.IsLifted ||
                binary.OperatorMethod is not null)
            {
                return false;
            }

            if (IsBoolean(binary.LeftOperand.Type))
            {
                return IsBoolean(binary.RightOperand.Type) && IsBoolean(binary.Type) &&
                    binary.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr or
                        BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr or
                        BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals;
            }

            if (!IsUInt32(binary.LeftOperand.Type))
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
            (conditional.Type is null || IsScalarLocal(conditional.Type));

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

        private static bool IsBoolean(ITypeSymbol? type) =>
            type?.SpecialType == SpecialType.System_Boolean;

        private static bool IsScalarLocal(ITypeSymbol? type) => IsUInt32(type) || IsBoolean(type);

        private static bool IsWritableScalar(IOperation target) => target switch
        {
            ILocalReferenceOperation local => IsScalarLocal(local.Local.Type),
            IParameterReferenceOperation parameter =>
                parameter.Parameter.RefKind == RefKind.None && IsUInt32(parameter.Parameter.Type),
            _ => false,
        };
    }
}
