namespace Warp.CSharp;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class WarpEntryPointAttribute : Attribute
{
    public WarpEntryPointAttribute(WarpExecution execution = WarpExecution.Map)
    {
        if (!Enum.IsDefined(execution))
        {
            throw new ArgumentOutOfRangeException(
                nameof(execution),
                execution,
                "The Warp execution mode is not registered.");
        }

        Execution = execution;
    }

    public WarpExecution Execution { get; }
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class WarpInputAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class WarpScalarAttribute : Attribute;
