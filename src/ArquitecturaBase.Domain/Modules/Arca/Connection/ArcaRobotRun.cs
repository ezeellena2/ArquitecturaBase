using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>De qué lado trabaja el robot de delegación (ADR 0026): con la clave del cliente o con la de la plataforma.</summary>
public enum ArcaRobotSide
{
    Company,
    Platform,
}

/// <summary>
/// El estado guardado de un lado del robot. «En curso» con la reserva vencida es una corrida interrumpida: el host que la
/// tenía se cayó.
/// </summary>
public enum ArcaRobotRunStatus
{
    Running,
    Waiting,
    Succeeded,
    Failed,
}

/// <summary>Por qué se frenó el robot, como un motivo estable (ADR 0026).</summary>
public enum ArcaRobotFailure
{
    LoginRejected,
    Captcha,
    ExtraValidation,
    ScreenChanged,
    Unavailable,
    Interrupted,
}

/// <summary>
/// Un lado del robot de delegación (ADR 0026), uno por conexión y lado. Guarda el avance, la reserva del host que lo corre
/// y el motivo de la falla, nunca la clave fiscal ni la CUIT o CUIL de quien entró. El lado del cliente corre en el host
/// que recibió la clave; el de la plataforma es durable y lo toma cualquier host con la reserva vencida o libre.
/// </summary>
public sealed class ArcaRobotRun : Entity
{
    private ArcaRobotRun() { }

    public Guid ConnectionId { get; private set; }
    public ArcaRobotSide Side { get; private set; }
    public ArcaRobotRunStatus Status { get; private set; }
    public string[] CompletedSteps { get; private set; } = [];
    public string? CurrentStep { get; private set; }

    /// <summary>
    /// Lo que dejó un paso para la pantalla, por ejemplo el punto de venta que dio de alta. Sin secretos. Sobrevive a reiniciar
    /// la corrida (O10): el punto de venta propio que guardó una pasada lo toma la siguiente.
    /// </summary>
    public Dictionary<string, string> Outputs { get; private set; } = new(StringComparer.Ordinal);
    public ArcaRobotFailure? Failure { get; private set; }
    public string? FailedStep { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public Guid? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public Guid? ConsentedBy { get; private set; }
    public DateTime? ConsentedAtUtc { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }

    /// <summary>El último aviso al dueño (motivo y paso) y cuándo salió; sobrevive a reiniciar la corrida.</summary>
    public string? AlertKey { get; private set; }
    public DateTime? AlertedAtUtc { get; private set; }

    public static ArcaRobotRun StartCompany(Guid connectionId, Guid consentedBy, Guid owner, DateTime nowUtc, TimeSpan lease)
    {
        var run = new ArcaRobotRun { ConnectionId = connectionId, Side = ArcaRobotSide.Company };
        run.BeginCompany(consentedBy, owner, nowUtc, lease);
        return run;
    }

    public static ArcaRobotRun SchedulePlatform(Guid connectionId, DateTime nowUtc) => new()
    {
        ConnectionId = connectionId,
        Side = ArcaRobotSide.Platform,
        Status = ArcaRobotRunStatus.Waiting,
        StartedAtUtc = nowUtc,
        NextAttemptAtUtc = nowUtc,
    };

    /// <summary>Vuelve a empezar el lado del cliente con otra clave; no mientras otro host lo esté corriendo.</summary>
    public Result RestartCompany(Guid consentedBy, Guid owner, DateTime nowUtc, TimeSpan lease)
    {
        if (IsHeld(nowUtc)) return ArcaConnectionErrors.RobotRunInProgress;
        BeginCompany(consentedBy, owner, nowUtc, lease);
        return Result.Success();
    }

    /// <summary>
    /// El cliente siguió con la guía e hizo a mano lo que faltaba (RB3-A): su lado, frenado o interrumpido, queda terminado.
    /// No mientras corre.
    /// </summary>
    public Result CompleteByHand(DateTime nowUtc)
    {
        if (Side != ArcaRobotSide.Company || !(Status == ArcaRobotRunStatus.Failed || IsInterrupted(nowUtc))) return ArcaConnectionErrors.RobotNotStopped;
        Status = ArcaRobotRunStatus.Succeeded;
        ClearFailure();
        Finish(nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// Vuelve a poner en espera el lado de la plataforma, vencido ya, salvo que haya terminado bien o que ARCA haya rechazado su
    /// ingreso (H5): ése lo rearma sólo <see cref="RearmPlatform"/>, porque unos pocos rechazos bloquean la clave.
    /// </summary>
    public void Reschedule(DateTime nowUtc)
    {
        if (Status == ArcaRobotRunStatus.Succeeded || IsHeld(nowUtc) || PlatformLoginRejected) return;
        StartOver(nowUtc);
    }

    /// <summary>
    /// Quien administra ARCA vuelve a probar la clave de la plataforma después de actualizarla (H5): sólo un lado de la
    /// plataforma frenado por ingreso rechazado.
    /// </summary>
    public Result RearmPlatform(DateTime nowUtc)
    {
        if (!PlatformLoginRejected) return ArcaConnectionErrors.RobotPlatformNotRejected;
        StartOver(nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// Hace vencido ahora un lado de la plataforma que espera, cuando terminó el lado del cliente: empieza otra etapa (aceptar,
    /// asignar y confirmar), con sus propios intentos.
    /// </summary>
    public void Wake(DateTime nowUtc)
    {
        if (Status != ArcaRobotRunStatus.Waiting) return;
        NextAttemptAtUtc = nowUtc;
        Attempts = 0;
    }

    public bool IsHeld(DateTime nowUtc) => Status == ArcaRobotRunStatus.Running && LeaseExpiresAtUtc > nowUtc;

    public bool IsInterrupted(DateTime nowUtc) => Status == ArcaRobotRunStatus.Running && LeaseExpiresAtUtc <= nowUtc;

    /// <summary>
    /// Toma el lado de la plataforma si le toca o si el host que lo tenía se cayó. Una pasada interrumpida cuenta como un intento
    /// (H19): agotados los intentos, el lado queda frenado como interrumpido, en el paso en que estaba, en vez de volver a tomarse.
    /// </summary>
    public bool TryReserve(Guid owner, DateTime nowUtc, TimeSpan lease, int maxAttempts)
    {
        var due = Status == ArcaRobotRunStatus.Waiting && NextAttemptAtUtc <= nowUtc;
        if (!due && !IsInterrupted(nowUtc)) return false;
        if (!due && Attempts >= maxAttempts)
        {
            Status = ArcaRobotRunStatus.Failed;
            Failure = ArcaRobotFailure.Interrupted;
            // Cada pasada empieza por el ingreso: si el host se cayó antes del primer paso, quedó en ése.
            FailedStep = CurrentStep ?? ArcaRobotSteps.PlatformLogin;
            Finish(nowUtc);
            return false;
        }

        Status = ArcaRobotRunStatus.Running;
        Attempts = checked(Attempts + 1);
        ClearFailure();
        Hold(owner, nowUtc, lease);
        return true;
    }

    /// <summary>H20: el latido de un paso que sigue corriendo renueva la reserva; sólo la propia y antes de que venza.</summary>
    public bool Renew(Guid owner, DateTime nowUtc, TimeSpan lease)
    {
        if (!Owns(owner, nowUtc)) return false;
        Hold(owner, nowUtc, lease);
        return true;
    }

    public bool Begin(string step, Guid owner, DateTime nowUtc, TimeSpan lease)
    {
        if (!Owns(owner, nowUtc)) return false;
        CurrentStep = step;
        Hold(owner, nowUtc, lease);
        return true;
    }

    /// <summary>O10: lo que un paso va a dar en ARCA, guardado antes de confirmarlo, con la reserva propia.</summary>
    public bool Record(string step, string output, Guid owner, DateTime nowUtc, TimeSpan lease)
    {
        if (!Owns(owner, nowUtc)) return false;
        Outputs = new(Outputs, StringComparer.Ordinal) { [step] = output };
        Hold(owner, nowUtc, lease);
        return true;
    }

    public bool Complete(string step, string? output, Guid owner, DateTime nowUtc, TimeSpan lease)
    {
        if (!Owns(owner, nowUtc)) return false;
        if (!CompletedSteps.Contains(step, StringComparer.Ordinal)) CompletedSteps = [.. CompletedSteps, step];
        if (output is not null) Outputs = new(Outputs, StringComparer.Ordinal) { [step] = output };
        CurrentStep = null;
        Hold(owner, nowUtc, lease);
        return true;
    }

    public bool Succeed(Guid owner, DateTime nowUtc)
    {
        if (!Owns(owner, nowUtc)) return false;
        Status = ArcaRobotRunStatus.Succeeded;
        Finish(nowUtc);
        return true;
    }

    public bool Fail(ArcaRobotFailure failure, string? step, Guid owner, DateTime nowUtc)
    {
        if (!Owns(owner, nowUtc)) return false;
        Status = ArcaRobotRunStatus.Failed;
        Failure = failure;
        FailedStep = step;
        Finish(nowUtc);
        return true;
    }

    /// <summary>El lado de la plataforma espera hasta <paramref name="nextAttemptAtUtc"/>; la falla, si la hay, queda a la vista.</summary>
    public bool Postpone(Guid owner, ArcaRobotFailure? failure, string? step, DateTime nextAttemptAtUtc)
    {
        if (LeaseOwner != owner || Status != ArcaRobotRunStatus.Running) return false;
        Status = ArcaRobotRunStatus.Waiting;
        Failure = failure;
        FailedStep = step;
        CurrentStep = null;
        NextAttemptAtUtc = nextAttemptAtUtc;
        ReleaseLease();
        return true;
    }

    /// <summary>La clave de un aviso: el motivo y el paso. La pantalla promete el aviso cuando la del último es la de la falla (H19).</summary>
    public static string AlertKeyFor(ArcaRobotFailure failure, string? step) => $"{failure}:{step}";

    public bool ShouldAlert(string key, DateTime nowUtc, TimeSpan interval) =>
        !string.Equals(AlertKey, key, StringComparison.Ordinal) || AlertedAtUtc is not { } last || nowUtc - last >= interval;

    public void Alerted(string key, DateTime nowUtc)
    {
        AlertKey = key;
        AlertedAtUtc = nowUtc;
    }

    public override string ToString() => $"ArcaRobotRun {{ side: {Side}, status: {Status}, step: {CurrentStep ?? FailedStep} }}";

    private void BeginCompany(Guid consentedBy, Guid owner, DateTime nowUtc, TimeSpan lease)
    {
        Status = ArcaRobotRunStatus.Running;
        ConsentedBy = consentedBy;
        ConsentedAtUtc = nowUtc;
        StartedAtUtc = nowUtc;
        FinishedAtUtc = null;
        CompletedSteps = [];
        CurrentStep = null;
        Attempts = 1;
        ClearFailure();
        Hold(owner, nowUtc, lease);
    }

    private bool Owns(Guid owner, DateTime nowUtc) => IsHeld(nowUtc) && LeaseOwner == owner;

    private bool PlatformLoginRejected =>
        Side == ArcaRobotSide.Platform && Status == ArcaRobotRunStatus.Failed && Failure == ArcaRobotFailure.LoginRejected;

    private void StartOver(DateTime nowUtc)
    {
        Status = ArcaRobotRunStatus.Waiting;
        Attempts = 0;
        NextAttemptAtUtc = nowUtc;
        ClearFailure();
        ReleaseLease();
        FinishedAtUtc = null;
    }

    private void Hold(Guid owner, DateTime nowUtc, TimeSpan lease)
    {
        LeaseOwner = owner;
        LeaseExpiresAtUtc = nowUtc.Add(lease);
    }

    private void Finish(DateTime nowUtc)
    {
        CurrentStep = null;
        FinishedAtUtc = nowUtc;
        NextAttemptAtUtc = null;
        ReleaseLease();
    }

    private void ReleaseLease()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    private void ClearFailure()
    {
        Failure = null;
        FailedStep = null;
    }
}
