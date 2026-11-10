using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>Cómo ve la pantalla un lado del robot; «interrumpida» es una corrida cuyo host se cayó.</summary>
public enum ArcaRobotRunState
{
    Running,
    Waiting,
    Succeeded,
    Failed,
    Interrupted,
}

/// <summary>
/// El robot de delegación tal como lo ve la pantalla (RB2-A y RB3-A): si está prendido en la instalación y cada lado con
/// sus pasos. Sin corrida, los dos lados son null.
/// </summary>
public sealed record ArcaRobotRunResponse(bool Enabled, ArcaRobotSideResponse? Company, ArcaRobotSideResponse? Platform)
{
    public static ArcaRobotRunResponse From(bool enabled, IReadOnlyCollection<ArcaRobotRunRow> runs, DateTime nowUtc) => new(enabled,
        Side(runs.SingleOrDefault(run => run.Side == ArcaRobotSide.Company), nowUtc),
        Side(runs.SingleOrDefault(run => run.Side == ArcaRobotSide.Platform), nowUtc));

    private static ArcaRobotSideResponse? Side(ArcaRobotRunRow? run, DateTime nowUtc) => run is null ? null : new(
        State(run, nowUtc), run.CurrentStep, run.CompletedSteps, run.Outputs,
        run.Status == ArcaRobotRunStatus.Running && run.LeaseExpiresAtUtc <= nowUtc ? ArcaRobotFailure.Interrupted : run.Failure,
        run.FailedStep ?? (run.Status == ArcaRobotRunStatus.Running && run.LeaseExpiresAtUtc <= nowUtc ? run.CurrentStep : null),
        run.Status == ArcaRobotRunStatus.Waiting ? run.NextAttemptAtUtc : null, run.StartedAtUtc, run.FinishedAtUtc);

    private static ArcaRobotRunState State(ArcaRobotRunRow run, DateTime nowUtc) => run.Status switch
    {
        ArcaRobotRunStatus.Running when run.LeaseExpiresAtUtc <= nowUtc => ArcaRobotRunState.Interrupted,
        ArcaRobotRunStatus.Running => ArcaRobotRunState.Running,
        ArcaRobotRunStatus.Waiting => ArcaRobotRunState.Waiting,
        ArcaRobotRunStatus.Succeeded => ArcaRobotRunState.Succeeded,
        _ => ArcaRobotRunState.Failed,
    };
}

/// <summary>
/// Un lado del robot: el paso en curso, los hechos (las claves de la guía), lo que dejó cada paso (el punto de venta), el
/// motivo y el paso de la falla, y cuándo vuelve a probar el lado de la plataforma.
/// </summary>
public sealed record ArcaRobotSideResponse(ArcaRobotRunState State, string? CurrentStep, IReadOnlyList<string> CompletedSteps,
    IReadOnlyDictionary<string, string> Outputs, ArcaRobotFailure? Failure, string? FailedStep, DateTime? NextAttemptAtUtc,
    DateTime StartedAtUtc, DateTime? FinishedAtUtc);

/// <summary>Un lado del robot como lo lee el lector, sin la reserva salvo su vencimiento.</summary>
public sealed record ArcaRobotRunRow(Guid Id, ArcaRobotSide Side, ArcaRobotRunStatus Status, IReadOnlyList<string> CompletedSteps,
    string? CurrentStep, IReadOnlyDictionary<string, string> Outputs, ArcaRobotFailure? Failure, string? FailedStep, int Attempts,
    DateTime? NextAttemptAtUtc, DateTime? LeaseExpiresAtUtc, DateTime StartedAtUtc, DateTime? FinishedAtUtc);
