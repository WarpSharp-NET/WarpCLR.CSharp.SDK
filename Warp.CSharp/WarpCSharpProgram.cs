using System.Collections.ObjectModel;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace Warp.CSharp;

public sealed class WarpCSharpProgram
{
    private readonly WarpLoadedModule module;
    private readonly ReadOnlyCollection<string> entryIdentities;

    private WarpCSharpProgram(WarpLoadedModule module)
    {
        this.module = module;
        entryIdentities = Array.AsReadOnly(
            module.Entries.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    public string ManifestHash => module.ManifestHash;

    public string AssemblyHash => module.AssemblyHash;

    public IReadOnlyList<string> EntryIdentities => entryIdentities;

    public static WarpCSharpProgram Load(
        string assemblyPath,
        string packageDirectory) => new(
            new WarpDevelopmentModuleLoader().Load(
                assemblyPath,
                packageDirectory));

    public static WarpCSharpProgram Load(
        ReadOnlyMemory<byte> assemblyBytes,
        string packageDirectory) => new(
            new WarpDevelopmentModuleLoader().Load(
                assemblyBytes,
                packageDirectory));

    public WarpCSharpSession CreateDevelopmentSession(WarpBackendKind backend) =>
        new(module, backend);
}
