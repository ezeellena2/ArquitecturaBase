using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;

/// <summary>
/// Los pasos guardados del robot (ADR 0026), cada uno en su transacción corta con el lock de la conexión. Los llaman el
/// servicio del robot y los workers de Infrastructure, con un scope por paso. Todo paso después de reservar vale sólo con la
/// reserva propia y devuelve false o null si otro host la tomó o venció: entonces no se guarda nada.
/// </summary>
public interface IArcaRobotRunStore
{
    /// <summary>
    /// Con una conexión en modo 3 y la revisión que vio la pantalla, reserva el lado del cliente para <paramref name="owner"/>
    /// (no mientras otro host lo corre) y pone en espera el de la plataforma.
    /// </summary>
    Task<Result<ArcaCompanyRobotWorkResponse>> ReserveCompanyAsync(Guid operatorId, Guid owner, long expectedRevision, CancellationToken cancellationToken);

    Task<bool> BeginStepAsync(Guid runId, Guid owner, string step, CancellationToken cancellationToken);

    Task<bool> CompleteStepAsync(Guid runId, Guid owner, string step, string? output, CancellationToken cancellationToken);

    /// <summary>
    /// Cierra el lado del cliente: bien, despierta al de la plataforma; frenado, guarda el motivo y el paso y avisa al dueño
    /// si corresponde (RB4-A).
    /// </summary>
    Task<bool> FinishCompanyAsync(Guid runId, Guid owner, ArcaRobotOutcome outcome, CancellationToken cancellationToken);

    /// <summary>Toma el lado de la plataforma si le toca y devuelve lo que el robot tiene que hacer; null si no le toca.</summary>
    Task<ArcaPlatformRobotWorkResponse?> ReservePlatformAsync(Guid runId, Guid owner, CancellationToken cancellationToken);

    /// <summary>Carga el certificado de la plataforma con la subida de siempre, si la reserva sigue siendo propia.</summary>
    Task<bool> InstallPlatformCertificateAsync(Guid runId, Guid owner, string certificatePem, CancellationToken cancellationToken);

    /// <summary>
    /// Cierra una pasada del lado de la plataforma: si ARCA no respondió, espera con su espera creciente; frenado, guarda el
    /// motivo y avisa. Bien y con <paramref name="verification"/> (la prueba de la conexión, la detección de la etapa 5)
    /// termina o espera a que ARCA lo refleje; bien y sin ella, porque el cliente no había terminado, lo espera.
    /// </summary>
    Task<bool> FinishPlatformAsync(Guid runId, Guid owner, ArcaRobotOutcome outcome, Result<ArcaTestResponse>? verification,
        CancellationToken cancellationToken);
}
