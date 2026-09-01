using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace WarpCLR.CSharp.Packaging.Tests;

internal sealed class WarpCLRPackageFixture : IDisposable
{
    private const string Configuration = "Release";
    private const string PackageVersion = "0.1.0";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly string[] WarpCLRPackageProjects =
    [
        "WarpCLR.IR/WarpCLR.IR.csproj",
        "WarpCLR.Runtime.Device/WarpCLR.Runtime.Device.csproj",
        "WarpCLR.Backend.Cpu/WarpCLR.Backend.Cpu.csproj",
        "WarpCLR.Backend.Nvidia/WarpCLR.Backend.Nvidia.csproj",
        "WarpCLR.Backend.Amd/WarpCLR.Backend.Amd.csproj",
        "WarpCLR.Backend.Intel/WarpCLR.Backend.Intel.csproj",
        "WarpCLR.Verifier/WarpCLR.Verifier.csproj",
        "WarpCLR.Compiler/WarpCLR.Compiler.csproj",
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

    public static WarpCLRPackageFixture Create()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string sdkRoot = FindRepositoryRoot();
            string warpClrRoot = Path.GetFullPath(
                Path.Combine(sdkRoot, "..", "WarpCLR"));
            string feed = Directory.CreateDirectory(
                Path.Combine(root, "feed")).FullName;

            BuildWarpCLRPackages(root, warpClrRoot, feed);
            BuildCSharpPackage(root, sdkRoot, feed);

            string packagePath = Path.Combine(
                feed,
                $"WarpCLR.CSharp.{PackageVersion}.nupkg");
            if (!File.Exists(packagePath))
            {
                throw new InvalidOperationException(
                    "The WarpCLR C# package was not created.");
            }

            string[] csharpPackages = Directory.GetFiles(
                    feed,
                    "WarpCLR.CSharp*.nupkg")
                .Select(Path.GetFileName)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()!;
            if (csharpPackages.Length != 1 ||
                csharpPackages[0] != $"WarpCLR.CSharp.{PackageVersion}.nupkg")
            {
                throw new InvalidOperationException(
                    "The feed contains an unexpected WarpCLR C# package.");
            }

            string validProject = CreateValidConsumer(root, feed);
            RunDotNet(
                root,
                Path.GetDirectoryName(validProject)!,
                ["restore", validProject, "--force", "--no-cache", "--verbosity", "minimal"]);
            ProcessResult validBuild = RunDotNet(
                root,
                Path.GetDirectoryName(validProject)!,
                ["build", validProject, "-c", Configuration, "--no-restore", "--verbosity", "minimal"]);
            if (!validBuild.Output.Contains(
                    "WarpCLR finalized and verified the assembly.",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The package build did not finalize the consumer assembly.");
            }

            ProcessResult incrementalBuild = RunDotNet(
                root,
                Path.GetDirectoryName(validProject)!,
                ["build", validProject, "-c", Configuration, "--no-restore", "--verbosity", "minimal"]);
            if (!incrementalBuild.Output.Contains(
                    "WarpCLR verified the finalized assembly.",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The incremental package build did not verify the assembly.");
            }

            string assemblyPath = Path.Combine(
                Path.GetDirectoryName(validProject)!,
                "bin",
                Configuration,
                "net10.0",
                "WarpCLRPackageConsumer.dll");
            byte[] consumerAssembly = File.ReadAllBytes(assemblyPath);

            string invalidProject = CreateInvalidConsumer(root, feed);
            RunDotNet(
                root,
                Path.GetDirectoryName(invalidProject)!,
                ["restore", invalidProject, "--force", "--no-cache", "--verbosity", "minimal"]);
            ProcessResult invalidBuild = RunDotNet(
                root,
                Path.GetDirectoryName(invalidProject)!,
                ["build", invalidProject, "-c", Configuration, "--no-restore", "--verbosity", "minimal"],
                requireSuccess: false);
            if (invalidBuild.ExitCode == 0 ||
                !invalidBuild.Output.Contains("WCS2001", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The packaged analyzer did not reject an unscoped allocation.");
            }

            return new WarpCLRPackageFixture(
                root,
                packagePath,
                consumerAssembly,
                invalidBuild.Output,
                incrementalBuild.Output,
                ReadPackageAssets(packagePath));
        }
        catch
        {
            DeleteTemporaryDirectory(root);
            throw;
        }
    }

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

    private static void BuildWarpCLRPackages(
        string root,
        string warpClrRoot,
        string feed)
    {
        RunDotNet(
            root,
            warpClrRoot,
            ["build", "WarpCLR.Runtime.Device/WarpCLR.Runtime.Device.csproj", "-c", Configuration, "--no-restore", "--verbosity", "minimal"]);
        RunDotNet(
            root,
            warpClrRoot,
            ["build", "WarpCLR.Runtime.Host/WarpCLR.Runtime.Host.csproj", "-c", Configuration, "--no-restore", "--verbosity", "minimal"]);
        RunDotNet(
            root,
            warpClrRoot,
            ["build", "WarpCLR.Sdk/WarpCLR.Sdk.csproj", "-c", Configuration, "--no-restore", "--verbosity", "minimal"]);

        foreach (string project in WarpCLRPackageProjects)
        {
            RunDotNet(
                root,
                warpClrRoot,
                ["pack", project, "-c", Configuration, "--no-build", "--no-restore", "-o", feed, "--verbosity", "quiet"]);
        }
    }

    private static void BuildCSharpPackage(
        string root,
        string sdkRoot,
        string feed)
    {
        const string project = "WarpCLR.CSharp/WarpCLR.CSharp.csproj";
        RunDotNet(
            root,
            sdkRoot,
            ["build", project, "-c", Configuration, "--no-restore", "--verbosity", "minimal"]);
        RunDotNet(
            root,
            sdkRoot,
            ["pack", project, "-c", Configuration, "--no-build", "--no-restore", "-o", feed, "--verbosity", "quiet"]);
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
                    [WarpScalar] uint scalar) => (value * 33u) + scalar;

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

                public static WarpReductionEntry Sum => WarpCLRKernelsEntries.Sum;

                public static WarpReductionEntry Minimum => WarpCLRKernelsEntries.Minimum;

                public static WarpReductionEntry Maximum => WarpCLRKernelsEntries.Maximum;
            }

            public static class MemoryFeature
            {
                public static uint RoundTrip(uint value)
                {
                    using WarpScope scope = WarpCLRMemory.Scope(16);
                    WarpScopedUInt32Array values = scope.AllocateUInt32Array(1);
                    values[0] = value;
                    return values[0];
                }
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
            Path.Combine(directory, "InvalidMemory.cs"),
            """
            using WarpCLR.CSharp;

            namespace Consumer;

            public static class InvalidMemory
            {
                public static uint AllocateWithoutUsing()
                {
                    WarpScope scope = WarpCLRMemory.Scope(16);
                    return (uint)scope.AllocationCount;
                }
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

    private static IReadOnlyList<string> ReadPackageAssets(string packagePath)
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

    private static ProcessResult RunDotNet(
        string root,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        bool requireSuccess = true)
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
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The dotnet process did not start.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The dotnet process exceeded 120 seconds.");
        }

        string output = standardOutput.GetAwaiter().GetResult() +
            standardError.GetAwaiter().GetResult();
        var result = new ProcessResult(process.ExitCode, output);
        if (requireSuccess && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The dotnet command failed with code {result.ExitCode}.{Environment.NewLine}{result.Output}");
        }

        return result;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(directory.FullName, "WarpCLR.CSharp.SDK.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
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

    private sealed record ProcessResult(int ExitCode, string Output);
}
