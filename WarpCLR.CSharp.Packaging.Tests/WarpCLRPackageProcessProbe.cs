using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace WarpCLR.CSharp.Packaging.Tests;

internal sealed class WarpCLRPackageProcessProbe : IDisposable
{
    private const string Source =
                """
                using System;
                using System.Diagnostics;
                using System.Globalization;
                using System.IO;
                using System.Threading;

                internal static class Probe
                {
                    private static int Main(string[] args)
                    {
                        Console.Out.WriteLine("warpclr-probe-stdout");
                        Console.Out.Flush();
                        Console.Error.WriteLine("warpclr-probe-stderr");
                        Console.Error.Flush();
                        if (args[0] is "descendant" or "descendant-finite")
                        {
                            using (Process self = Process.GetCurrentProcess())
                            {
                                File.WriteAllText("descendant.identity", self.Id.ToString(CultureInfo.InvariantCulture) + "\n" +
                                    self.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
                            }

                            Console.Out.WriteLine("warpclr-descendant-stdout");
                            Console.Out.Flush();
                            Console.Error.WriteLine("warpclr-descendant-stderr");
                            Console.Error.Flush();
                            File.WriteAllText("descendant.ready", "ready");
                            var lease = Stopwatch.StartNew();
                            TimeSpan duration = args[0] == "descendant-finite" ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(30);
                            while (!File.Exists("descendant.release") && lease.Elapsed < duration)
                            {
                                Thread.Sleep(10);
                            }

                            File.WriteAllText("descendant.exiting", "exiting");
                            return 0;
                        }

                        if (args[0] is "orphan-exit" or "orphan-hang" or "orphan-finite" or "spawn-descendant")
                        {
                            var childInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                            childInfo.ArgumentList.Add("exec");
                            childInfo.ArgumentList.Add("--runtimeconfig");
                            childInfo.ArgumentList.Add(args[1]);
                            childInfo.ArgumentList.Add(typeof(Probe).Assembly.Location);
                            childInfo.ArgumentList.Add(args[0] == "orphan-hang" ? "spawn-descendant" :
                                args[0] == "orphan-finite" ? "descendant-finite" : "descendant");
                            childInfo.ArgumentList.Add(args[1]);
                            using Process child = Process.Start(childInfo)!;
                            if (args[0] == "orphan-hang")
                            {
                                if (!child.WaitForExit(10000))
                                {
                                    return 18;
                                }

                                Thread.Sleep(Timeout.Infinite);
                            }

                            var ready = Stopwatch.StartNew();
                            while (!File.Exists("descendant.ready") && ready.Elapsed < TimeSpan.FromSeconds(10))
                            {
                                Thread.Sleep(10);
                            }

                            Console.Out.WriteLine("warpclr-parent-exiting");
                            Console.Out.Flush();
                            return File.Exists("descendant.ready") ? 0 : 19;
                        }

                        if (args[0] == "hang")
                        {
                            Thread.Sleep(Timeout.Infinite);
                        }

                        if (args[0] == "flood-success")
                        {
                            Console.Out.Write(new string('X', 100000));
                            Console.Error.Write(new string('Y', 100000));
                        }

                        return args[0] == "failure" ? 17 : 0;
                    }
                }
                """;

    public WarpCLRPackageProcessProbe()
    {
        DirectoryPath = Directory.CreateTempSubdirectory("warpclr-package-process-").FullName;
        try
        {
            CSharpCompilation compilation = RoslynCompilationFactory.Create("WarpCLRPackageProcessProbe", Source)
                .WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication,
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
        startInfo.ArgumentList.Add(Path.ChangeExtension(typeof(WarpCLRPackageProcessProbe).Assembly.Location, ".runtimeconfig.json"));
        return startInfo;
    }

    public async Task ReleaseDescendantAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await File.WriteAllTextAsync(Path.Combine(DirectoryPath, "descendant.release"), "release", deadline.Token).ConfigureAwait(false);
        string identityPath = Path.Combine(DirectoryPath, "descendant.identity");
        if (!File.Exists(identityPath))
        {
            return;
        }

        string[] identity = await File.ReadAllLinesAsync(identityPath, deadline.Token).ConfigureAwait(false);
        try
        {
            using Process descendant = Process.GetProcessById(int.Parse(identity[0], System.Globalization.CultureInfo.InvariantCulture));
            // No PID is signalled. Only the uniquely owned private release lease is mutated.
            await descendant.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // The recorded process already exited.
        }

        while (!File.Exists(Path.Combine(DirectoryPath, "descendant.exiting")))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), deadline.Token).ConfigureAwait(false);
        }
    }

    public async Task<bool> IsDescendantRunningAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string[] identity = await File.ReadAllLinesAsync(Path.Combine(DirectoryPath, "descendant.identity"),
            deadline.Token).ConfigureAwait(false);
        try
        {
            using Process descendant = Process.GetProcessById(int.Parse(identity[0], System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("Probe descendant recorded start ticks: " + identity[1] +
                "; observed start ticks: " + descendant.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return !descendant.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
