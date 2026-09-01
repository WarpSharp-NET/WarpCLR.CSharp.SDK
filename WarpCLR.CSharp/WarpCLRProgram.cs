using System.Collections.ObjectModel;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.CSharp;

public sealed class WarpCLRProgram
{
    private readonly WarpLoadedModule module;
    private readonly ReadOnlyCollection<string> entryIdentities;

    private WarpCLRProgram(WarpLoadedModule module)
    {
        this.module = module;
        entryIdentities = Array.AsReadOnly(
            module.Entries.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    public string ManifestHash => module.ManifestHash;

    public string AssemblyHash => module.AssemblyHash;

    public IReadOnlyList<string> EntryIdentities => entryIdentities;

    public static WarpCLRProgram Load(
        string assemblyPath,
        string packageDirectory) => new(
            new WarpDevelopmentModuleLoader().Load(
                assemblyPath,
                packageDirectory));

    public static WarpCLRProgram Load(
        ReadOnlyMemory<byte> assemblyBytes,
        string packageDirectory) => new(
            new WarpDevelopmentModuleLoader().Load(
                assemblyBytes,
                packageDirectory));

    public WarpCLRSession CreateDevelopmentSession(WarpBackendKind backend) =>
        new(module, backend);
}
