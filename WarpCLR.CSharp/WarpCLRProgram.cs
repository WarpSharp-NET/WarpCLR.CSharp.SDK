using System.Collections.ObjectModel;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.CSharp;

public sealed class WarpCLRProgram
{
    private readonly WarpLoadedModule? developmentModule;
    private readonly WarpRuntimeModule? runtimeModule;
    private readonly ReadOnlyCollection<string> entryIdentities;

    private WarpCLRProgram(WarpLoadedModule module)
    {
        developmentModule = module;
        entryIdentities = Array.AsReadOnly(
            module.Entries.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    private WarpCLRProgram(WarpRuntimeModule module)
    {
        runtimeModule = module;
        entryIdentities = Array.AsReadOnly(
            module.Entries.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    public string ManifestHash => runtimeModule?.ManifestHash ?? developmentModule!.ManifestHash;

    public string AssemblyHash => runtimeModule?.AssemblyHash ?? developmentModule!.AssemblyHash;

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

    public static WarpCLRProgram LoadTrusted(ReadOnlyMemory<byte> assemblyBytes, WarpModuleTrust trust) =>
        new(WarpRuntimeModule.Load(assemblyBytes, trust));

    public WarpCLRSession CreateDevelopmentSession(WarpBackendKind backend)
    {
        if (developmentModule is null)
        {
            throw new WarpHostException("WRPRUNTIME1004", "A development session requires an explicitly loaded development artifact package.");
        }

        return new WarpCLRSession(developmentModule, backend);
    }

    public WarpCLRRuntimeSession CreateRuntimeSession(
        WarpBackendKind backend,
        WarpRuntimeOptions? options = null,
        WarpJitCache? jitCache = null)
    {
        if (runtimeModule is null)
        {
            throw new WarpHostException("WRPRUNTIME1000", "A runtime session requires explicit module authorization through LoadTrusted.");
        }

        return new WarpCLRRuntimeSession(runtimeModule, backend, options, jitCache);
    }
}
