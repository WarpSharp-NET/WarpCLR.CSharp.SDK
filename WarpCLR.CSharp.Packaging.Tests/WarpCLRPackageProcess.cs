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
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The package child process did not start.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            string captured = Capture(await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
            var failure = new TimeoutException(FormattableString.Invariant(
                $"Package child exceeded {deadline.TotalSeconds} seconds; elapsed {clock.Elapsed}; PID {process.Id}.{Environment.NewLine}") +
                Describe(startInfo) + captured, exception);
            failure.Data["WarpCLR.ChildProcessId"] = process.Id;
            throw failure;
        }

        string output = Capture(await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
        var result = new WarpCLRPackageProcessResult(process.ExitCode, output, clock.Elapsed);
        if (requireSuccess && result.ExitCode != 0)
        {
            throw new InvalidOperationException("Package child failed with code " +
                result.ExitCode.ToString(CultureInfo.InvariantCulture) + "." + Environment.NewLine + Describe(startInfo) + result.Output);
        }

        return result;
    }

    private static string Capture(string standardOutput, string standardError) =>
        "stdout:" + Environment.NewLine + standardOutput + Environment.NewLine + "stderr:" + Environment.NewLine + standardError;

    private static string Describe(ProcessStartInfo startInfo) =>
        "Working directory: " + startInfo.WorkingDirectory + Environment.NewLine +
        "Command: " + startInfo.FileName + " " + string.Join(' ', startInfo.ArgumentList) + Environment.NewLine;
}
