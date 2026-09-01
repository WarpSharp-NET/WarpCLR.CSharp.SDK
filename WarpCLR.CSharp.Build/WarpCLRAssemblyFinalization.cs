using WarpCLR.Verifier;

namespace WarpCLR.CSharp.Build;

internal sealed class WarpCLRAssemblyFinalization
{
    public WarpCLRAssemblyFinalization(
        byte[] assemblyBytes,
        WarpVerifiedModule? module,
        bool hasManifest,
        bool changed)
    {
        AssemblyBytes = assemblyBytes;
        Module = module;
        HasManifest = hasManifest;
        Changed = changed;
    }

    public byte[] AssemblyBytes { get; }

    public WarpVerifiedModule? Module { get; }

    public bool HasManifest { get; }

    public bool Changed { get; }
}
