using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace WarpCLR.CSharp.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class WarpCLRGenerator : IIncrementalGenerator
{
    private const string EntryAttributeName =
        "WarpCLR.CSharp.WarpEntryPointAttribute";
    private const string InputAttributeName =
        "WarpCLR.CSharp.WarpInputAttribute";
    private const string ScalarAttributeName =
        "WarpCLR.CSharp.WarpScalarAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<WarpCLREntryModel> entries = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EntryAttributeName,
                static (node, _) => node is MethodDeclarationSyntax,
                static (syntaxContext, _) => CreateEntry(syntaxContext));

        IncrementalValueProvider<(Compilation Left, ImmutableArray<WarpCLREntryModel> Right)> input =
            context.CompilationProvider.Combine(entries.Collect());
        context.RegisterSourceOutput(
            input,
            static (productionContext, value) => Generate(
                productionContext,
                value.Left,
                value.Right));
    }

    private static WarpCLREntryModel CreateEntry(
        GeneratorAttributeSyntaxContext context)
    {
        var method = (IMethodSymbol)context.TargetSymbol;
        AttributeData entryAttribute = context.Attributes[0];
        int execution = entryAttribute.ConstructorArguments.Length == 1 &&
            entryAttribute.ConstructorArguments[0].Value is int value
                ? value
                : 0;

        var roles = ImmutableArray.CreateBuilder<string>(method.Parameters.Length);
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            bool input = HasAttribute(parameter, InputAttributeName);
            bool scalar = HasAttribute(parameter, ScalarAttributeName);
            roles.Add(input == scalar ? "invalid" : input ? "input" : "scalar");
        }

        string namespaceIdentity = method.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : method.ContainingNamespace.ToDisplayString();
        string typeIdentity = namespaceIdentity.Length == 0
            ? method.ContainingType.MetadataName
            : $"{namespaceIdentity}.{method.ContainingType.MetadataName}";
        string identity = $"{typeIdentity}.{method.MetadataName}";

        return new WarpCLREntryModel(
            typeIdentity,
            method.MetadataName,
            GetNamespaceSource(method.ContainingNamespace),
            $"WarpCLR{SanitizeIdentifier(method.ContainingType.Name)}Entries",
            method.ContainingType.DeclaredAccessibility == Accessibility.Public
                ? "public"
                : "internal",
            EscapeIdentifier(method.Name),
            execution,
            roles.MoveToImmutable(),
            ComputeGraphHashPlaceholder(identity));
    }

    private static void Generate(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<WarpCLREntryModel> entries)
    {
        if (entries.IsDefaultOrEmpty)
        {
            return;
        }

        ImmutableArray<WarpCLREntryModel> ordered = entries
            .OrderBy(entry => entry.Identity, StringComparer.Ordinal)
            .ToImmutableArray();
        string producer = compilation.AssemblyName ?? "WarpCLR.CSharp";
        string producerVersion = compilation.Assembly.Identity.Version.ToString();
        string manifest = WarpCLRManifestWriter.Write(
            producer,
            producerVersion,
            ordered);
        string source = WarpCLRSourceWriter.Write(manifest, ordered);
        context.AddSource(
            "WarpCLR.CSharp.Entries.g.cs",
            SourceText.From(source, Encoding.UTF8));
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName) => symbol
        .GetAttributes()
        .Any(attribute => string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            metadataName,
            StringComparison.Ordinal));

    private static string GetNamespaceSource(INamespaceSymbol namespaceSymbol)
    {
        if (namespaceSymbol.IsGlobalNamespace)
        {
            return string.Empty;
        }

        var parts = new Stack<string>();
        for (INamespaceSymbol? current = namespaceSymbol;
             current is not null && !current.IsGlobalNamespace;
             current = current.ContainingNamespace)
        {
            parts.Push(EscapeIdentifier(current.Name));
        }

        return string.Join(".", parts);
    }

    private static string EscapeIdentifier(string value)
    {
        if (!SyntaxFacts.IsValidIdentifier(value))
        {
            return $"WarpCLR{SanitizeIdentifier(value)}";
        }

        return SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ||
            SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
                ? $"@{value}"
                : value;
    }

    private static string SanitizeIdentifier(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            result.Append(SyntaxFacts.IsIdentifierPartCharacter(character)
                ? character
                : '_');
        }

        return result.ToString();
    }

    private static string ComputeGraphHashPlaceholder(string identity)
    {
        byte[] input = Encoding.UTF8.GetBytes(
            $"WarpCLR.CSharp.GraphPlaceholder/0.1\0{identity}");
        byte[] hash;
        using (SHA256 sha256 = SHA256.Create())
        {
            hash = sha256.ComputeHash(input);
        }

        const string hex = "0123456789ABCDEF";
        var result = new char[hash.Length * 2];
        for (int index = 0; index < hash.Length; index++)
        {
            result[index * 2] = hex[hash[index] >> 4];
            result[(index * 2) + 1] = hex[hash[index] & 0x0F];
        }

        return new string(result);
    }
}
