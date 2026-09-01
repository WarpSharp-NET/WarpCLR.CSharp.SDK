namespace WarpCLR.CSharp.Build;

internal sealed class WarpCLRBuildException : Exception
{
    public WarpCLRBuildException(string code, string message)
        : base($"{code}: {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
