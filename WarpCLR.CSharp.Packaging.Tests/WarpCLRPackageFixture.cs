using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text;

namespace WarpCLR.CSharp.Packaging.Tests;

internal sealed class WarpCLRPackageFixture : IDisposable
{
    private const string Configuration = "Release";
    private const string PackageVersion = "0.1.0";
    private const string CoreCLRWorkerProject = "WarpCLR.CoreCLR.Worker/WarpCLR.CoreCLR.Worker.csproj";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly string[] CoreCLRWorkerFiles =
    [
        "WarpCLR.CoreCLR.Worker.dll",
        "WarpCLR.CoreCLR.Worker.deps.json",
        "WarpCLR.CoreCLR.Worker.runtimeconfig.json",
    ];
    private static readonly string[] WarpCLRBuildProjects =
    [
        "WarpCLR.IR/WarpCLR.IR.csproj",
        "WarpCLR.Backend.CoreCLR/WarpCLR.Backend.CoreCLR.csproj",
        "WarpCLR.Backend.NVPTX/WarpCLR.Backend.NVPTX.csproj",
        "WarpCLR.Backend.AMDGPU/WarpCLR.Backend.AMDGPU.csproj",
        "WarpCLR.Backend.SPIRV/WarpCLR.Backend.SPIRV.csproj",
        "WarpCLR.Verifier/WarpCLR.Verifier.csproj",
        "WarpCLR.Compiler/WarpCLR.Compiler.csproj",
        CoreCLRWorkerProject,
        "WarpCLR.Runtime.Host/WarpCLR.Runtime.Host.csproj",
        "WarpCLR.Sdk/WarpCLR.Sdk.csproj",
    ];

    private WarpCLRPackageFixture(
        string root,
        string packagePath,
        byte[] consumerAssembly,
        string invalidBuildOutput,
        string incrementalBuildOutput,
        IReadOnlyList<string> packageAssets)
    {
        Root = root;
        PackagePath = packagePath;
        ConsumerAssembly = consumerAssembly;
        InvalidBuildOutput = invalidBuildOutput;
        IncrementalBuildOutput = incrementalBuildOutput;
        PackageAssets = packageAssets;
    }

    public string Root { get; }

    public string PackagePath { get; }

    public byte[] ConsumerAssembly { get; }

    public string InvalidBuildOutput { get; }

    public string IncrementalBuildOutput { get; }

    public IReadOnlyList<string> PackageAssets { get; }

    public static async Task<WarpCLRPackageFixture> CreateAsync()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string sdkRoot = FindRepositoryRoot();
            string warpClrRoot = Path.GetFullPath(Path.Combine(sdkRoot, "..", "WarpCLR"));
            string feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
            await BuildWarpCLRPackagesAsync(root, warpClrRoot, feed).ConfigureAwait(false);
            await BuildCSharpPackageAsync(root, sdkRoot, feed).ConfigureAwait(false);
            return await BuildConsumerFixtureAsync(root, feed, ValidatePackageFeed(feed)).ConfigureAwait(false);
        }
        catch
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("WARP_PACKAGE_TEST_RETAIN_FAILURES"), "1", StringComparison.Ordinal))
            {
                DeleteTemporaryDirectory(root);
            }

            throw;
        }
    }

    private static string ValidatePackageFeed(string feed)
    {
        string packagePath = Path.Combine(feed, $"WarpCLR.CSharp.{PackageVersion}.nupkg");
        if (!File.Exists(packagePath))
        {
            throw new InvalidOperationException("The WarpCLR C# package was not created.");
        }

        string[] csharpPackages = Directory.GetFiles(feed, "WarpCLR.CSharp*.nupkg")
            .Select(Path.GetFileName)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray()!;
        if (csharpPackages.Length != 1 ||
            !string.Equals(csharpPackages[0], $"WarpCLR.CSharp.{PackageVersion}.nupkg", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The feed contains an unexpected WarpCLR C# package.");
        }

        return packagePath;
    }

    private static async Task<WarpCLRPackageFixture> BuildConsumerFixtureAsync(
        string root, string feed, string packagePath)
    {
        string validProject = CreateValidConsumer(root, feed);
        await RestoreConsumerAsync(root, validProject).ConfigureAwait(false);
        WarpCLRPackageProcessResult validBuild = await BuildConsumerAsync(root, validProject).ConfigureAwait(false);
        if (!validBuild.Output.Contains("WarpCLR finalized and verified the assembly.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The package build did not finalize the consumer assembly.");
        }

        await ValidateCoreCLRWorkerDeploymentAsync(root, feed).ConfigureAwait(false);

        WarpCLRPackageProcessResult incrementalBuild = await BuildConsumerAsync(root, validProject).ConfigureAwait(false);
        if (!incrementalBuild.Output.Contains("WarpCLR verified the finalized assembly.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The incremental package build did not verify the assembly.");
        }

        string assemblyPath = Path.Combine(root, "artifacts", "bin", "Consumer", "release", "WarpCLRPackageConsumer.dll");
        byte[] consumerAssembly = await File.ReadAllBytesAsync(assemblyPath).ConfigureAwait(false);
        string invalidProject = CreateInvalidConsumer(root, feed);
        await RestoreConsumerAsync(root, invalidProject).ConfigureAwait(false);
        WarpCLRPackageProcessResult invalidBuild = await BuildConsumerAsync(root, invalidProject, requireSuccess: false).ConfigureAwait(false);
        if (invalidBuild.ExitCode == 0 || !invalidBuild.Output.Contains("WCS1003", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The packaged analyzer did not reject an operation outside the portable profile.");
        }

        return new WarpCLRPackageFixture(root, packagePath, consumerAssembly, invalidBuild.Output,
            incrementalBuild.Output, ReadPackageAssets(packagePath));
    }

    private static async Task ValidateCoreCLRWorkerDeploymentAsync(string root, string feed)
    {
        using ZipArchive package = await ZipFile.OpenReadAsync(Path.Combine(feed, $"WarpCLR.Runtime.Host.{PackageVersion}.nupkg")).ConfigureAwait(false);
        foreach (string file in CoreCLRWorkerFiles)
        {
            ZipArchiveEntry entry = package.GetEntry("tools/coreclr-worker/" + file)
                ?? throw new InvalidOperationException($"The runtime package omits {file}.");
            using Stream input = await entry.OpenAsync().ConfigureAwait(false);
            using MemoryStream expected = new();
            await input.CopyToAsync(expected).ConfigureAwait(false);
            byte[] actual = await File.ReadAllBytesAsync(Path.Combine(root, "artifacts", "bin", "Consumer", "release", file)).ConfigureAwait(false);
            if (!actual.AsSpan().SequenceEqual(expected.ToArray()))
            {
                throw new InvalidOperationException($"The consumer did not receive the exact packaged {file}.");
            }
        }
    }

    private static Task<WarpCLRPackageProcessResult> RestoreConsumerAsync(string root, string project) =>
        RunDotNetAsync(root, Path.GetDirectoryName(project)!,
            ["restore", project, "--force", "--no-cache", "--verbosity", "minimal"]);

    private static Task<WarpCLRPackageProcessResult> BuildConsumerAsync(
        string root, string project, bool requireSuccess = true) =>
        RunDotNetAsync(root, Path.GetDirectoryName(project)!,
            ["build", project, "-c", Configuration, "--no-restore", "--verbosity", "minimal"], requireSuccess);

    public string CreateRuntimeDirectory(string name)
    {
        string path = Path.Combine(
            Root,
            "runtime",
            name,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose() => DeleteTemporaryDirectory(Root);

    private static async Task BuildWarpCLRPackagesAsync(
        string root,
        string warpClrRoot,
        string feed)
    {
        await RunDotNetAsync(
            root,
            warpClrRoot,
            ["restore", "WarpCLR.slnx", "--force", "--no-cache", "--verbosity", "minimal"]).ConfigureAwait(false);
        // Build each restored project in dependency order. The finite child deadline
        // covers one analyzer-enabled project, not a cold transitive graph.
        foreach (string project in WarpCLRBuildProjects)
        {
            await RunDotNetAsync(root, warpClrRoot,
                ["build", project, "-c", Configuration, "--no-restore", "--verbosity", "minimal",
                    "-p:BuildProjectReferences=false", "--disable-build-servers", "-m:1", "-nr:false",
                    "-p:UseSharedCompilation=false"]).ConfigureAwait(false);
        }

        foreach (string project in WarpCLRBuildProjects)
        {
            if (string.Equals(project, CoreCLRWorkerProject, StringComparison.Ordinal))
            {
                continue;
            }

            await RunDotNetAsync(
                root,
                warpClrRoot,
                ["pack", project, "-c", Configuration, "--no-build", "--no-restore", "-o", feed, "--verbosity", "quiet"]).ConfigureAwait(false);
        }
    }

    private static async Task BuildCSharpPackageAsync(
        string root,
        string sdkRoot,
        string feed)
    {
        const string project = "WarpCLR.CSharp/WarpCLR.CSharp.csproj";
        await RunDotNetAsync(
            root,
            sdkRoot,
            ["restore", project, "--force", "--no-cache", "--verbosity", "minimal"]).ConfigureAwait(false);
        string[] projects =
        [
            "WarpCLR.CSharp.Analyzers/WarpCLR.CSharp.Analyzers.csproj",
            "WarpCLR.CSharp.Build/WarpCLR.CSharp.Build.csproj",
            "WarpCLR.CSharp.Generators/WarpCLR.CSharp.Generators.csproj",
            project,
        ];
        foreach (string buildProject in projects)
        {
            await RunDotNetAsync(root, sdkRoot,
                ["build", buildProject, "-c", Configuration, "--no-restore", "--verbosity", "minimal",
                    "-p:BuildProjectReferences=false", "--disable-build-servers", "-m:1", "-nr:false",
                    "-p:UseSharedCompilation=false"]).ConfigureAwait(false);
        }
        await RunDotNetAsync(
            root,
            sdkRoot,
            ["pack", project, "-c", Configuration, "--no-build", "--no-restore", "-o", feed, "--verbosity", "quiet"]).ConfigureAwait(false);
    }

    private static string CreateValidConsumer(string root, string feed)
    {
        string directory = Directory.CreateDirectory(
            Path.Combine(root, "valid-consumer")).FullName;
        string projectPath = Path.Combine(directory, "Consumer.csproj");
        WriteConsumerProject(projectPath, root, feed, "WarpCLRPackageConsumer");
        File.WriteAllText(
            Path.Combine(directory, "Kernels.cs"),
            """
            using WarpCLR.CSharp;

            namespace Consumer;

            public static class Kernels
            {
                [WarpEntryPoint]
                public static uint Transform(
                    [WarpInput] uint value,
                    [WarpScalar] uint scalar) => Mix(value, scalar);

                private static uint Mix(uint value, uint scalar) =>
                    (value * 33u) + scalar;

                [WarpEntryPoint]
                public static uint Select(
                    [WarpInput] uint value,
                    [WarpScalar] uint threshold) =>
                    value <= threshold ? value + 1u : value - 1u;

                [WarpEntryPoint(WarpExecution.ReduceWrappingSum)]
                public static uint Sum([WarpInput] uint value) => value;

                [WarpEntryPoint(WarpExecution.ReduceMinimum)]
                public static uint Minimum([WarpInput] uint value) => value;

                [WarpEntryPoint(WarpExecution.ReduceMaximum)]
                public static uint Maximum([WarpInput] uint value) => value;
            }

            public static class CatalogFeature
            {
                public static WarpMapEntry Map => WarpCLRKernelsEntries.Transform;

                public static WarpMapEntry Conditional => WarpCLRKernelsEntries.Select;

                public static WarpReductionEntry Sum => WarpCLRKernelsEntries.Sum;

                public static WarpReductionEntry Minimum => WarpCLRKernelsEntries.Minimum;

                public static WarpReductionEntry Maximum => WarpCLRKernelsEntries.Maximum;
            }

            """,
            Utf8WithoutBom);
        return projectPath;
    }

    private static string CreateInvalidConsumer(string root, string feed)
    {
        string directory = Directory.CreateDirectory(
            Path.Combine(root, "invalid-consumer")).FullName;
        string projectPath = Path.Combine(directory, "Consumer.csproj");
        WriteConsumerProject(projectPath, root, feed, "WarpCLRInvalidConsumer");
        File.WriteAllText(
            Path.Combine(directory, "InvalidKernel.cs"),
            """
            using WarpCLR.CSharp;

            namespace Consumer;

            public static class InvalidKernel
            {
                [WarpEntryPoint]
                public static uint Divide(
                    [WarpInput] uint value,
                    [WarpScalar] uint divisor) => value / divisor;
            }
            """,
            Utf8WithoutBom);
        return projectPath;
    }

    private static void WriteConsumerProject(
        string projectPath,
        string root,
        string feed,
        string assemblyName)
    {
        string packages = Path.Combine(root, "consumer-packages");
        string project = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{{EscapeXml(assemblyName)}}</AssemblyName>
                <LangVersion>14.0</LangVersion>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <Deterministic>true</Deterministic>
                <RestoreSources>{{EscapeXml(feed)}}</RestoreSources>
                <RestorePackagesPath>{{EscapeXml(packages)}}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="WarpCLR.CSharp" Version="0.1.0" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(projectPath, project, Utf8WithoutBom);
    }

    private static string[] ReadPackageAssets(string packagePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        return archive.Entries
            .Select(entry => entry.FullName)
            .Where(
                name => name.StartsWith("analyzers/", StringComparison.Ordinal) ||
                        name.StartsWith("build/", StringComparison.Ordinal) ||
                        name.StartsWith("lib/", StringComparison.Ordinal) ||
                        name.StartsWith("tools/", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<WarpCLRPackageProcessResult> RunDotNetAsync(
        string root, string workingDirectory, IReadOnlyList<string> arguments, bool requireSuccess = true)
    {
        WarpCLRPackageProcessResult result = await WarpCLRPackageProcess.RunAsync(
            CreateDotNetStartInfo(root, workingDirectory, arguments), TimeSpan.FromSeconds(120), requireSuccess).ConfigureAwait(false);
        Console.WriteLine($"Package child completed in {result.Elapsed}: dotnet {string.Join(' ', arguments)}");
        Console.WriteLine(result.Output);
        return result;
    }

    private static ProcessStartInfo CreateDotNetStartInfo(
        string root, string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add("--artifacts-path");
        startInfo.ArgumentList.Add(Path.Combine(root, "artifacts"));

        string processTemp = Directory.CreateDirectory(
            Path.Combine(root, "process-temp")).FullName;
        string cliHome = Directory.CreateDirectory(
            Path.Combine(root, "dotnet-home")).FullName;
        startInfo.Environment["TEMP"] = processTemp;
        startInfo.Environment["TMP"] = processTemp;
        startInfo.Environment["DOTNET_CLI_HOME"] = cliHome;
        startInfo.Environment["NUGET_PACKAGES"] = Path.Combine(
            root,
            "consumer-packages");
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment.Remove("WarpBuildRoot");

        return startInfo;
    }

    private static string FindRepositoryRoot()
    {
        AssemblyMetadataAttribute[] roots = typeof(WarpCLRPackageFixture).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => string.Equals(attribute.Key, "WarpCLR.CSharp.RepositoryRoot", StringComparison.Ordinal))
            .ToArray();
        Assert.HasCount(1, roots, "The test assembly must declare one SDK repository root.");
        string? root = roots[0].Value;
        if (root is not null &&
            File.Exists(Path.Combine(root, "WarpCLR.CSharp.SDK.slnx")))
        {
            return root;
        }

        throw new InvalidOperationException(
            "The WarpCLR C# SDK repository root was not found.");
    }

    private static string EscapeXml(string value) =>
        SecurityElement.Escape(value)
        ?? throw new InvalidOperationException("The XML value could not be escaped.");

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "WarpCLR.CSharp.Packaging.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
