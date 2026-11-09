using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// Lo que una familia necesita de la conexión para llamar a un servicio fuera de la prueba, en nombre de la empresa: el
/// certificado activo y su ticket, y el error de cada respuesta que no está lista. Las familias lo usan; la conexión no
/// las conoce.
/// </summary>
internal sealed class ArcaServiceSessionIssuer(IArcaConnectionReader reader, IArcaAccessTicketProvider tickets, TimeProvider timeProvider)
{
    public async Task<Guid?> FindConnectionIdAsync(CancellationToken cancellationToken) => (await reader.FindAsync(cancellationToken))?.Id;

    public async Task<Result<ArcaServiceSession>> OpenAsync(ArcaServiceCode service, CancellationToken cancellationToken)
    {
        if (await reader.FindSigningCertificateAsync(ArcaCertificateRole.Active, cancellationToken) is not { } certificate)
            return await reader.FindAsync(cancellationToken) is null ? ArcaConnectionErrors.NotConnected : ArcaConnectionErrors.CertificateNotLoaded;

        // Una sesión es para operar en nombre de la empresa: en modo delegación, el ticket tiene que incluirla (ADR 0027).
        // Una consulta con la CUIT de la plataforma (el padrón fuera de la prueba) necesitaría su propia apertura.
        var reference = certificate.ToRef(new HashSet<string>(StringComparer.Ordinal) { service.Value });
        var outcome = await tickets.GetAsync(reference, service, cancellationToken);
        return outcome is ArcaTicketOutcome.Ready ticket ? new ArcaServiceSession(reference, ticket) : Failure(outcome);
    }

    /// <summary>El error de una respuesta de un servicio que no está lista.</summary>
    public static Error Failure(ArcaServiceCheckStatus status) => status switch
    {
        ArcaServiceCheckStatus.NotAuthorized => ArcaConnectionErrors.ServiceNotAuthorized,
        ArcaServiceCheckStatus.NotDelegated => ArcaConnectionErrors.ServiceNotDelegated,
        ArcaServiceCheckStatus.ClockSkew => ArcaConnectionErrors.ClockSkew,
        ArcaServiceCheckStatus.Unavailable => ArcaConnectionErrors.Unavailable,
        _ => ArcaConnectionErrors.ServiceRejected,
    };

    private Error Failure(ArcaTicketOutcome outcome) => outcome switch
    {
        ArcaTicketOutcome.Blocked blocked => ArcaConnectionErrors.ServiceBlocked(
            Math.Max(1, (int)Math.Ceiling((blocked.UntilUtc - timeProvider.GetUtcNow().UtcDateTime).TotalSeconds))),
        ArcaTicketOutcome.NotAuthorized => ArcaConnectionErrors.ServiceNotAuthorized,
        ArcaTicketOutcome.NotDelegated => ArcaConnectionErrors.ServiceNotDelegated,
        ArcaTicketOutcome.ClockSkew => ArcaConnectionErrors.ClockSkew,
        _ => ArcaConnectionErrors.Unavailable,
    };
}
