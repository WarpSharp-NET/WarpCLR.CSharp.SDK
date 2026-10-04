using System.Diagnostics;

namespace WarpCLR.CSharp.Packaging.Tests;

internal sealed class WarpCLRPackageProcessExecution(Process process, WarpCLRPackageProcessLifetime lifetime)
{
    internal static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(2);
    private readonly WarpCLRPackageProcessCapture standardOutput = new();
    private readonly WarpCLRPackageProcessCapture standardError = new();

    public string Output => "stdout:" + Environment.NewLine + standardOutput.Snapshot() + Environment.NewLine +
        "stderr:" + Environment.NewLine + standardError.Snapshot();

    public string Cleanup { get; private set; } = string.Empty;

    public bool CleanupIncomplete { get; private set; }

    public bool RootExitObserved { get; private set; }

    public bool ReadersStopped { get; private set; }

    public bool StandardOutputEndOfStream => standardOutput.EndOfStream;

    public bool StandardErrorEndOfStream => standardError.EndOfStream;

    public async Task<Exception?> RunAsync(CancellationToken deadline)
    {
        using var stopReaders = new CancellationTokenSource();
        using var stopRootWait = new CancellationTokenSource();
        Task stdout = standardOutput.DrainAsync(process.StandardOutput, stopReaders.Token);
        Task stderr = standardError.DrainAsync(process.StandardError, stopReaders.Token);
        Task exited = process.WaitForExitAsync(stopRootWait.Token);
        Task operation = Task.WhenAll(exited, stdout, stderr);
        Exception failure;
        try
        {
            await operation.WaitAsync(deadline).ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
        {
            failure = error;
        }

        using var cleanupDeadline = new CancellationTokenSource(CleanupDeadline);
        Task<string> termination = lifetime.TerminateAsync();
        try
        {
            Cleanup = await termination.WaitAsync(cleanupDeadline.Token).ConfigureAwait(false);
            await stopReaders.CancelAsync().WaitAsync(cleanupDeadline.Token).ConfigureAwait(false);
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            await Task.WhenAll(exited, stdout, stderr).WaitAsync(cleanupDeadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
        {
            Cleanup += " Cleanup did not complete within " + CleanupDeadline + ": " + error.Message;
            CleanupIncomplete = true;
        }
        finally
        {
            try
            {
                await ShutdownAsync(stopReaders, stopRootWait, cleanupDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CleanupIncomplete = true;
                Cleanup += " Cancellation callback cleanup exceeded its budget.";
            }
        }

        RootExitObserved = exited.IsCompletedSuccessfully;
        ReadersStopped = stdout.IsCompleted && stderr.IsCompleted;
        CleanupIncomplete |= !RootExitObserved || !ReadersStopped || !standardOutput.EndOfStream || !standardError.EndOfStream ||
            Cleanup.StartsWith("Tree termination failed:", StringComparison.Ordinal);
        Cleanup += " Root exit observed: " + RootExitObserved + "; readers stopped: " + ReadersStopped +
            "; stdout EOF: " + standardOutput.EndOfStream + "; stderr EOF: " + standardError.EndOfStream +
            ". Remaining descendant cleanup is unverified; the caller owns containment/recovery of any surviving descendants.";
        return failure;
    }

    private async Task ShutdownAsync(CancellationTokenSource stopReaders, CancellationTokenSource stopRootWait, CancellationToken deadline)
    {
        Task cancellation = Task.WhenAll(stopReaders.CancelAsync(), stopRootWait.CancelAsync());
        process.StandardOutput.Dispose();
        process.StandardError.Dispose();
        await cancellation.WaitAsync(deadline).ConfigureAwait(false);
    }
}
