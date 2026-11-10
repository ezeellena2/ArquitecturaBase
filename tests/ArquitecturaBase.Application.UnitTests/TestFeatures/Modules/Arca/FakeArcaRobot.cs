using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;

/// <summary>Los lados del robot en memoria, con los mismos contratos que EF; las escrituras exigen la unidad de trabajo.</summary>
internal sealed class FakeArcaRobotRuns : IArcaRobotRunRepository, IArcaRobotRunReader
{
    public Func<bool> InTransaction { get; set; } = () => true;
    public List<ArcaRobotRun> Runs { get; } = [];

    public ArcaRobotRun? Company => Runs.SingleOrDefault(run => run.Side == ArcaRobotSide.Company);
    public ArcaRobotRun? Platform => Runs.SingleOrDefault(run => run.Side == ArcaRobotSide.Platform);

    public Task<ArcaRobotRun?> GetAsync(Guid connectionId, ArcaRobotSide side, CancellationToken cancellationToken) =>
        Task.FromResult(Runs.SingleOrDefault(run => run.ConnectionId == connectionId && run.Side == side));

    public Task<ArcaRobotRun?> GetAsync(Guid runId, CancellationToken cancellationToken) => Task.FromResult(Runs.SingleOrDefault(run => run.Id == runId));

    public void Add(ArcaRobotRun run)
    {
        Assert.True(InTransaction(), "ARCA robot writes happen inside the unit of work.");
        Runs.Add(run);
    }

    public Task<IReadOnlyList<ArcaRobotRunRow>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ArcaRobotRunRow>>(
        [.. Runs.Select(run => new ArcaRobotRunRow(run.Id, run.Side, run.Status, [.. run.CompletedSteps], run.CurrentStep,
            new Dictionary<string, string>(run.Outputs, StringComparer.Ordinal), run.Failure, run.FailedStep, run.Attempts, run.NextAttemptAtUtc,
            run.LeaseExpiresAtUtc, run.StartedAtUtc, run.FinishedAtUtc))]);

    public Task<IReadOnlyList<Guid>> ListDuePlatformRunsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. Runs.Where(run => run.Side == ArcaRobotSide.Platform
            && ((run.Status == ArcaRobotRunStatus.Waiting && run.NextAttemptAtUtc <= nowUtc) || run.IsInterrupted(nowUtc))).Select(run => run.Id)]);
}

/// <summary>La cola en memoria del host: guarda lo encolado y, con <see cref="WhenEnqueued"/>, deja ver el momento del encolado.</summary>
internal sealed class FakeArcaRobotQueue : IArcaCompanyRobotQueue
{
    public List<(ArcaCompanyRobotWorkResponse Job, ArcaFiscalCredentials Credentials)> Jobs { get; } = [];
    public bool Accepts { get; set; } = true;
    public Action? WhenEnqueued { get; set; }

    public bool TryEnqueue(ArcaCompanyRobotWorkResponse job, ArcaFiscalCredentials credentials)
    {
        WhenEnqueued?.Invoke();
        if (!Accepts) return false;
        Jobs.Add((job, credentials));
        return true;
    }
}
