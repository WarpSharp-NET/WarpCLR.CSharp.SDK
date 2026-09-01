using System.Collections.ObjectModel;
using WarpCLR.IR;
using WarpCLR.Runtime.Host;

namespace WarpCLR.CSharp;

public enum WarpCLRStructuredExecutionMode
{
    CoreClrReference,
    SemanticEmulation,
}

public sealed class WarpCLRStructuredExecutionException : Exception
{
    internal WarpCLRStructuredExecutionException(
        WarpStructuredExecutionException exception)
        : base(exception.Message, exception.InnerException)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Code = exception.Code;
        StageIdentity = exception.StageIdentity;
        WorkItemIndex = exception.WorkItemIndex;
    }

    public string Code { get; }

    public string StageIdentity { get; }

    public int WorkItemIndex { get; }
}

public sealed class WarpCLRStructuredProgramBuilder
{
    private readonly List<WarpStructuredReferenceStage> stages = [];

    public WarpCLRStructuredProgramBuilder AddStage(
        string identity,
        int workItemCount,
        Action<int> body)
    {
        stages.Add(
            new WarpStructuredReferenceStage(
                identity,
                workItemCount,
                body));
        return this;
    }

    public WarpCLRStructuredProgram Build() => new(stages);
}

public sealed class WarpCLRStructuredProgram
{
    private readonly ReadOnlyCollection<string> stageIdentities;

    internal WarpCLRStructuredProgram(
        IEnumerable<WarpStructuredReferenceStage> stages)
    {
        RuntimeProgram = new WarpStructuredReferenceProgram(stages);
        stageIdentities = Array.AsReadOnly(
            RuntimeProgram.Stages
                .Select(stage => stage.Identity)
                .ToArray());
    }

    public IReadOnlyList<string> StageIdentities => stageIdentities;

    internal WarpStructuredReferenceProgram RuntimeProgram { get; }
}

public sealed class WarpCLRStructuredSession
{
    private readonly WarpStructuredReferenceSession session;

    internal WarpCLRStructuredSession(
        WarpBackendKind backend,
        WarpDevelopmentExecutionMode mode,
        int maximumConcurrency)
    {
        session = new WarpStructuredReferenceSession(
            backend,
            mode,
            maximumConcurrency);
    }

    public WarpBackendKind Backend => session.Backend;

    public WarpCLRStructuredExecutionMode Mode => session.Mode switch
    {
        WarpDevelopmentExecutionMode.CoreClrReference =>
            WarpCLRStructuredExecutionMode.CoreClrReference,
        WarpDevelopmentExecutionMode.SemanticEmulation =>
            WarpCLRStructuredExecutionMode.SemanticEmulation,
        _ => throw new InvalidOperationException(
            $"Execution mode '{session.Mode}' is not registered."),
    };

    public int MaximumConcurrency => session.MaximumConcurrency;

    public void Execute(
        WarpCLRStructuredProgram program,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);

        try
        {
            session.Execute(program.RuntimeProgram, cancellationToken);
        }
        catch (WarpStructuredExecutionException exception)
        {
            throw new WarpCLRStructuredExecutionException(exception);
        }
    }
}

public static class WarpCLRStructuredRuntime
{
    public static WarpCLRStructuredSession CreateCpuReferenceSession(
        int maximumConcurrency = -1) => new(
            WarpBackendKind.CpuReference,
            WarpDevelopmentExecutionMode.CoreClrReference,
            maximumConcurrency);

    public static WarpCLRStructuredSession CreateDevelopmentSession(
        WarpBackendKind backend,
        int maximumConcurrency = -1) => new(
            backend,
            backend == WarpBackendKind.CpuReference
                ? WarpDevelopmentExecutionMode.CoreClrReference
                : WarpDevelopmentExecutionMode.SemanticEmulation,
            maximumConcurrency);
}
