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
        Assert.IsInstanceOfType<int>(error.Data["WarpCLR.ChildProcessId"]);
        Assert.IsInstanceOfType<TimeSpan>(error.Data["WarpCLR.ChildElapsed"]);
        Assert.IsGreaterThan(TimeSpan.Zero, (TimeSpan)error.Data["WarpCLR.ChildElapsed"]!);
        StringAssert.Contains(error.Message, "PID " + error.Data["WarpCLR.ChildProcessId"], StringComparison.Ordinal);
        StringAssert.Contains(error.Message, "elapsed " + error.Data["WarpCLR.ChildElapsed"], StringComparison.Ordinal);
        Assert.AreEqual(17, error.Data["WarpCLR.ChildExitCode"]);
        Console.WriteLine(error.Message);
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
    [DataRow("orphan-exit")]
    [DataRow("orphan-hang")]
    public async Task InheritedPipesCannotExtendTheOperationOrCleanupDeadline(string mode)
    {
        using var probe = new WarpCLRPackageProcessProbe();
        var clock = Stopwatch.StartNew();
        try
        {
            TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() =>
                WarpCLRPackageProcess.RunAsync(probe.StartInfo(mode), TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            Assert.IsLessThan(TimeSpan.FromSeconds(10), clock.Elapsed);
            AssertOutput(error.Message);
            StringAssert.Contains(error.Message, "warpclr-descendant-stdout", StringComparison.Ordinal);
            StringAssert.Contains(error.Message, "warpclr-descendant-stderr", StringComparison.Ordinal);
            Assert.IsFalse(IsRunning((int)error.Data["WarpCLR.ChildProcessId"]!));
            Assert.IsTrue((bool)error.Data["WarpCLR.CleanupIncomplete"]!);
            Assert.IsTrue((bool)error.Data["WarpCLR.RootExitObserved"]!);
            Assert.IsTrue((bool)error.Data["WarpCLR.ReadersStopped"]!);
            Assert.IsFalse((bool)error.Data["WarpCLR.StandardOutputEndOfStream"]!);
            Assert.IsFalse((bool)error.Data["WarpCLR.StandardErrorEndOfStream"]!);
            Assert.IsFalse((bool)error.Data["WarpCLR.DescendantCleanupConfirmed"]!);
            Assert.IsTrue(await probe.IsDescendantRunningAsync().ConfigureAwait(false));
            StringAssert.Contains(error.Message, "descendant cleanup is unverified", StringComparison.Ordinal);
            StringAssert.Contains(error.Message, (string)error.Data["WarpCLR.CleanupDisposition"]!, StringComparison.Ordinal);
            StringAssert.Contains(error.Message, "elapsed " + error.Data["WarpCLR.ChildElapsed"], StringComparison.Ordinal);
            Console.WriteLine(mode + ": " + error.Message);
        }
        finally
        {
            await probe.ReleaseDescendantAsync().ConfigureAwait(false);
        }

        Assert.IsFalse(await probe.IsDescendantRunningAsync().ConfigureAwait(false));
    }

    [TestMethod]
    public async Task InheritedPipesThatCloseWithinTheDeadlineCompleteNormally()
    {
        using var probe = new WarpCLRPackageProcessProbe();
        try
        {
            WarpCLRPackageProcessResult result = await WarpCLRPackageProcess.RunAsync(
                probe.StartInfo("orphan-finite"), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.AreEqual(0, result.ExitCode);
            AssertOutput(result.Output);
            StringAssert.Contains(result.Output, "warpclr-parent-exiting", StringComparison.Ordinal);
            StringAssert.Contains(result.Output, "warpclr-descendant-stdout", StringComparison.Ordinal);
            StringAssert.Contains(result.Output, "warpclr-descendant-stderr", StringComparison.Ordinal);
        }
        finally
        {
            await probe.ReleaseDescendantAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task LargeOutputIsDrainedWithExplicitBoundedDiagnosticPrefixes()
    {
        using var probe = new WarpCLRPackageProcessProbe();
        WarpCLRPackageProcessResult result = await WarpCLRPackageProcess.RunAsync(
            probe.StartInfo("flood-success"), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.AreEqual(0, result.ExitCode);
        AssertOutput(result.Output);
        const string marker = "[diagnostic output truncated]";
        StringAssert.Contains(result.Output, marker, StringComparison.Ordinal);
        Assert.AreNotEqual(result.Output.IndexOf(marker, StringComparison.Ordinal), result.Output.LastIndexOf(marker, StringComparison.Ordinal));
        Assert.IsLessThan(132000, result.Output.Length);
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
