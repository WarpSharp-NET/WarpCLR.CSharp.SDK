using WarpCLR.IR;

namespace WarpCLR.CSharp.Tests;

[TestClass]
public sealed class StructuredExecutionFeatureTests
{
    [TestMethod]
    [FourBackends]
    public void Structured_program_joins_each_parallel_stage(
        WarpBackendKind backend)
    {
        var values = new int[513];
        var observedIncompleteValue = 0;
        WarpCLRStructuredProgram program =
            new WarpCLRStructuredProgramBuilder()
                .AddStage(
                    "produce",
                    values.Length,
                    index => values[index] = index + 1)
                .AddStage(
                    "consume",
                    values.Length,
                    index =>
                    {
                        if (values[index] != index + 1)
                        {
                            Interlocked.Exchange(
                                ref observedIncompleteValue,
                                1);
                        }

                        values[index] *= 2;
                    })
                .Build();

        WarpCLRStructuredSession session =
            WarpCLRStructuredRuntime.CreateDevelopmentSession(backend);
        session.Execute(program);

        Assert.AreEqual(backend, session.Backend);
        Assert.AreEqual(0, observedIncompleteValue);
        CollectionAssert.AreEqual(
            Enumerable.Range(1, values.Length)
                .Select(value => value * 2)
                .ToArray(),
            values);
        CollectionAssert.AreEqual(
            new[] { "produce", "consume" },
            program.StageIdentities.ToArray());
    }

    [TestMethod]
    [FourBackends]
    public void Structured_session_reports_its_explicit_execution_mode(
        WarpBackendKind backend)
    {
        WarpCLRStructuredSession session =
            WarpCLRStructuredRuntime.CreateDevelopmentSession(
                backend,
                maximumConcurrency: 1);

        Assert.AreEqual(backend, session.Backend);
        Assert.AreEqual(1, session.MaximumConcurrency);
        Assert.AreEqual(
            backend == WarpBackendKind.CpuReference
                ? WarpCLRStructuredExecutionMode.CoreClrReference
                : WarpCLRStructuredExecutionMode.SemanticEmulation,
            session.Mode);
    }

    [TestMethod]
    [FourBackends]
    public void Structured_session_exposes_deterministic_failure_authority(
        WarpBackendKind backend)
    {
        var nextStageCount = 0;
        WarpCLRStructuredProgram program =
            new WarpCLRStructuredProgramBuilder()
                .AddStage(
                    "fault",
                    12,
                    index =>
                    {
                        if (index is 8 or 2)
                        {
                            throw new InvalidOperationException(
                                $"failure-{index}");
                        }
                    })
                .AddStage(
                    "must-not-run",
                    1,
                    _ => Interlocked.Increment(ref nextStageCount))
                .Build();

        WarpCLRStructuredExecutionException exception =
            Assert.ThrowsExactly<WarpCLRStructuredExecutionException>(
                () => WarpCLRStructuredRuntime
                    .CreateDevelopmentSession(backend)
                    .Execute(program));

        Assert.AreEqual("WRPHOST1100", exception.Code);
        Assert.AreEqual("fault", exception.StageIdentity);
        Assert.AreEqual(2, exception.WorkItemIndex);
        Assert.AreEqual("failure-2", exception.InnerException?.Message);
        Assert.AreEqual(0, nextStageCount);
    }

    [TestMethod]
    [FourBackends]
    public void Cpu_reference_factory_selects_the_real_reference_identity(
        WarpBackendKind backend)
    {
        Assert.IsTrue(WarpBackendCatalog.Required.Contains(backend));

        WarpCLRStructuredSession session =
            WarpCLRStructuredRuntime.CreateCpuReferenceSession();

        Assert.AreEqual(WarpBackendKind.CpuReference, session.Backend);
        Assert.AreEqual(
            WarpCLRStructuredExecutionMode.CoreClrReference,
            session.Mode);
    }
}
