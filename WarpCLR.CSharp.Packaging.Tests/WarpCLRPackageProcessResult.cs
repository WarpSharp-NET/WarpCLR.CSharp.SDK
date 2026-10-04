namespace WarpCLR.CSharp.Packaging.Tests;

internal sealed record WarpCLRPackageProcessResult(int ExitCode, string Output, TimeSpan Elapsed);
