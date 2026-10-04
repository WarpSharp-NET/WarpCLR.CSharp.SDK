using System.ComponentModel;
using System.Diagnostics;

namespace WarpCLR.CSharp.Packaging.Tests;

internal sealed class WarpCLRPackageProcessLifetime(Process process) : IDisposable
{
    private readonly Lock gate = new();
    private bool terminationPending;
    private bool disposeRequested;

    public Task<string> TerminateAsync()
    {
        lock (gate) { terminationPending = true; }
        return Task.Run(() =>
        {
            try
            {
                if (process.HasExited)
                {
                    return "Root already exited; no remaining descendants can be identified from the root.";
                }

                process.Kill(entireProcessTree: true);
                return "Tree termination requested; this does not confirm descendant exit.";
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException or AggregateException)
            {
                return "Tree termination failed: " + error;
            }
            finally
            {
                lock (gate)
                {
                    terminationPending = false;
                    if (disposeRequested) { process.Dispose(); }
                }
            }
        });
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposeRequested = true;
            // A non-preemptible OS call may outlast the cleanup budget. Its task owns the
            // process handle until it returns; never race disposal against that call.
            if (!terminationPending) { process.Dispose(); }
        }
    }
}
