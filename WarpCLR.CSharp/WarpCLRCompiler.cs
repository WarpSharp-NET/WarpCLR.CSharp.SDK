using WarpCLR.Sdk;

namespace WarpCLR.CSharp;

public static class WarpCLRCompiler
{
    public static WarpModuleCompilation CompileModule(string assemblyPath) =>
        new WarpBuildPipeline().CompileModule(assemblyPath);

    public static WarpModuleCompilation CompileModule(
        ReadOnlyMemory<byte> assemblyBytes) =>
        new WarpBuildPipeline().CompileModule(assemblyBytes);

    public static WarpAotPackage CompilePackage(string assemblyPath) =>
        new WarpBuildPipeline().CompilePackage(assemblyPath);

    public static WarpAotPackage CompilePackage(
        ReadOnlyMemory<byte> assemblyBytes) =>
        new WarpBuildPipeline().CompilePackage(assemblyBytes);
}
