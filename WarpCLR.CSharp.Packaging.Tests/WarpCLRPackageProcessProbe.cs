using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace WarpCLR.CSharp.Packaging.Tests;

internal sealed class WarpCLRPackageProcessProbe : IDisposable
{
    public WarpCLRPackageProcessProbe()
    {
        DirectoryPath = Directory.CreateTempSubdirectory("warpclr-package-process-").FullName;
        try
        {
            CSharpCompilation compilation = RoslynCompilationFactory.Create("WarpCLRPackageProcessProbe",
                """
                using System;
                using System.Threading;

                internal static class Probe
                {
                    private static int Main(string[] args)
                    {
                        Console.Out.WriteLine("warpclr-probe-stdout");
                        Console.Out.Flush();
                        Console.Error.WriteLine("warpclr-probe-stderr");
                        Console.Error.Flush();
                        if (args[0] == "hang")
                        {
                            Thread.Sleep(Timeout.Infinite);
                        }

                        return args[0] == "failure" ? 17 : 0;
                    }
                }
                """).WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                    optimizationLevel: OptimizationLevel.Release, deterministic: true));
            using var image = new MemoryStream();
            EmitResult result = compilation.Emit(image);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            File.WriteAllBytes(Path.Combine(DirectoryPath, "Probe.dll"), image.ToArray());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string DirectoryPath { get; }

    public ProcessStartInfo StartInfo(string mode)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = DirectoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--runtimeconfig");
        startInfo.ArgumentList.Add(Path.ChangeExtension(typeof(WarpCLRPackageProcessProbe).Assembly.Location, ".runtimeconfig.json"));
        startInfo.ArgumentList.Add(Path.Combine(DirectoryPath, "Probe.dll"));
        startInfo.ArgumentList.Add(mode);
        return startInfo;
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
