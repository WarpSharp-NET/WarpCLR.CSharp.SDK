namespace WarpCLR.CSharp.Build;

internal sealed class WarpCLRBuildException : Exception
{
    public WarpCLRBuildException()
        : this("WCSB0000", "The WarpCLR build failed.")
    {
    }

    public WarpCLRBuildException(string message)
        : this("WCSB0000", message)
    {
    }

    public WarpCLRBuildException(string message, Exception innerException)
        : base($"WCSB0000: {message}", innerException)
    {
        Code = "WCSB0000";
    }

    public WarpCLRBuildException(string code, string message)
        : base($"{code}: {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
