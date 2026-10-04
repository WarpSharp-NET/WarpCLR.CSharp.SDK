using System.Diagnostics;
using System.Globalization;

namespace WarpCLR.CSharp.Packaging.Tests;

internal static class WarpCLRPackageProcess
{
    internal static async Task<WarpCLRPackageProcessResult> RunAsync(
        ProcessStartInfo startInfo, TimeSpan deadline, bool requireSuccess = true)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deadline, TimeSpan.Zero);
        if (!startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError || startInfo.UseShellExecute)
        {
            throw new ArgumentException("Package children require redirected streams and no shell execution.", nameof(startInfo));
        }

        using var timeout = new CancellationTokenSource(deadline);
        var clock = Stopwatch.StartNew();
        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The package child process did not start.");
        using var lifetime = new WarpCLRPackageProcessLifetime(process);
        var execution = new WarpCLRPackageProcessExecution(process, lifetime);
        Exception? exception = await execution.RunAsync(timeout.Token).ConfigureAwait(false);
        if (exception is not null)
        {
            string heading = timeout.IsCancellationRequested
                ? FormattableString.Invariant($"Package child exceeded {deadline.TotalSeconds} seconds")
                : "Package child exit/output observation failed";
            TimeSpan elapsed = clock.Elapsed;
            string message = heading + FailureIdentity(process.Id, elapsed) + Describe(startInfo) +
                "Cleanup: " + execution.Cleanup + Environment.NewLine + execution.Output;
            Exception failure = timeout.IsCancellationRequested
                ? new TimeoutException(message, exception)
                : new InvalidOperationException(message, exception);
            AddFailureData(failure, process.Id, elapsed);
            failure.Data["WarpCLR.CleanupIncomplete"] = execution.CleanupIncomplete;
            failure.Data["WarpCLR.CleanupDisposition"] = execution.Cleanup;
            failure.Data["WarpCLR.RootExitObserved"] = execution.RootExitObserved;
            failure.Data["WarpCLR.ReadersStopped"] = execution.ReadersStopped;
            failure.Data["WarpCLR.StandardOutputEndOfStream"] = execution.StandardOutputEndOfStream;
            failure.Data["WarpCLR.StandardErrorEndOfStream"] = execution.StandardErrorEndOfStream;
            failure.Data["WarpCLR.DescendantCleanupConfirmed"] = false;
            throw failure;
        }

        var result = new WarpCLRPackageProcessResult(process.ExitCode, execution.Output, clock.Elapsed);
        if (requireSuccess && result.ExitCode != 0)
        {
            var failure = new InvalidOperationException("Package child failed with code " +
                result.ExitCode.ToString(CultureInfo.InvariantCulture) + FailureIdentity(process.Id, result.Elapsed) +
                Describe(startInfo) + result.Output);
            AddFailureData(failure, process.Id, result.Elapsed);
            failure.Data["WarpCLR.ChildExitCode"] = result.ExitCode;
            throw failure;
        }

        return result;
    }

    private static string FailureIdentity(int pid, TimeSpan elapsed) =>
        FormattableString.Invariant($"; elapsed {elapsed}; PID {pid}.{Environment.NewLine}");

    private static void AddFailureData(Exception failure, int pid, TimeSpan elapsed)
    {
        failure.Data["WarpCLR.ChildProcessId"] = pid;
        failure.Data["WarpCLR.ChildElapsed"] = elapsed;
    }

    private static string Describe(ProcessStartInfo startInfo) =>
        "Working directory: " + startInfo.WorkingDirectory + Environment.NewLine +
        "Command: " + startInfo.FileName + " " + string.Join(' ', startInfo.ArgumentList) + Environment.NewLine;
}
