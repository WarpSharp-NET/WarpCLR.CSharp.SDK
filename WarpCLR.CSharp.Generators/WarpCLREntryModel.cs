using System.Collections.Immutable;

namespace WarpCLR.CSharp.Generators;

internal sealed class WarpCLREntryModel
{
    public WarpCLREntryModel(
        string typeIdentity,
        string methodIdentity,
        string namespaceSource,
        string catalogName,
        string catalogAccessibility,
        string propertyName,
        int execution,
        ImmutableArray<string> parameterRoles,
        string graphHashPlaceholder)
    {
        TypeIdentity = typeIdentity;
        MethodIdentity = methodIdentity;
        NamespaceSource = namespaceSource;
        CatalogName = catalogName;
        CatalogAccessibility = catalogAccessibility;
        PropertyName = propertyName;
        Execution = execution;
        ParameterRoles = parameterRoles;
        GraphHashPlaceholder = graphHashPlaceholder;
    }

    public string TypeIdentity { get; }

    public string MethodIdentity { get; }

    public string Identity => $"{TypeIdentity}.{MethodIdentity}";

    public string NamespaceSource { get; }

    public string CatalogName { get; }

    public string CatalogAccessibility { get; }

    public string PropertyName { get; }

    public int Execution { get; }

    public ImmutableArray<string> ParameterRoles { get; }

    public int InputBufferCount => ParameterRoles.Count(role => role == "input");

    public int ScalarArgumentCount => ParameterRoles.Count(role => role == "scalar");

    public string GraphHashPlaceholder { get; }
}
