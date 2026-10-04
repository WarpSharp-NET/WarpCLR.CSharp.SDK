using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace WarpCLR.CSharp.Packaging.Tests;

[TestClass]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "MSTest discovers and executes this child-process fixture through reflection.")]
internal sealed class WarpCLRPackageProcessTests
{
    [TestMethod]
    public async Task SuccessfulChildCapturesBothStreamsAndElapsedTime()
    {
        using var probe = new WarpCLRPackageProcessProbe();
        WarpCLRPackageProcessResult result = await WarpCLRPackageProcess.RunAsync(
            probe.StartInfo("success"), TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        Assert.AreEqual(0, result.ExitCode);
        AssertOutput(result.Output);
        Assert.IsGreaterThan(TimeSpan.Zero, result.Elapsed);
    }

    [TestMethod]
    public async Task FailedChildRetainsCommandWorkingDirectoryAndBothStreams()
    {
        using var probe = new WarpCLRPackageProcessProbe();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WarpCLRPackageProcess.RunAsync(probe.StartInfo("failure"), TimeSpan.FromSeconds(30))).ConfigureAwait(false);
        AssertOutput(error.Message);
        StringAssert.Contains(error.Message, "code 17", StringComparison.Ordinal);
        StringAssert.Contains(error.Message, "Command: dotnet exec", StringComparison.Ordinal);
        StringAssert.Contains(error.Message, probe.DirectoryPath, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ExpectedNonzeroExitCanBeInspectedWithoutLosingDiagnostics()
    {
        using var probe = new WarpCLRPackageProcessProbe();
        WarpCLRPackageProcessResult result = await WarpCLRPackageProcess.RunAsync(
            probe.StartInfo("failure"), TimeSpan.FromSeconds(30), requireSuccess: false).ConfigureAwait(false);
        Assert.AreEqual(17, result.ExitCode);
        AssertOutput(result.Output);
    }

    [TestMethod]
    public async Task TimedOutChildRetainsBothStreamsAndIsReapedBeforeTheExceptionReturns()
    {
        using var probe = new WarpCLRPackageProcessProbe();
        TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() =>
            WarpCLRPackageProcess.RunAsync(probe.StartInfo("hang"), TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        AssertOutput(error.Message);
        StringAssert.Contains(error.Message, "exceeded 2 seconds", StringComparison.Ordinal);
        StringAssert.Contains(error.Message, "Command: dotnet exec", StringComparison.Ordinal);
        StringAssert.Contains(error.Message, probe.DirectoryPath, StringComparison.Ordinal);
        Assert.IsFalse(IsRunning((int)error.Data["WarpCLR.ChildProcessId"]!));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task InvalidDeadlineIsRejectedBeforeLaunchingAnyChild(int seconds)
    {
        var startInfo = new ProcessStartInfo("warpclr-process-that-does-not-exist")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            WarpCLRPackageProcess.RunAsync(startInfo, TimeSpan.FromSeconds(seconds))).ConfigureAwait(false);
    }

    private static void AssertOutput(string output)
    {
        StringAssert.Contains(output, "stdout:", StringComparison.Ordinal);
        StringAssert.Contains(output, "warpclr-probe-stdout", StringComparison.Ordinal);
        StringAssert.Contains(output, "stderr:", StringComparison.Ordinal);
        StringAssert.Contains(output, "warpclr-probe-stderr", StringComparison.Ordinal);
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
