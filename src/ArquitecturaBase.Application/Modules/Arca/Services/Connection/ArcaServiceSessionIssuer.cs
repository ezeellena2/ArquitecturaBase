using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// Lo que una familia necesita de la conexión para llamar a un servicio fuera de la prueba: el certificado activo y su
/// ticket, y el error de cada respuesta que no está lista. Las familias lo usan; la conexión no las conoce.
/// </summary>
internal sealed class ArcaServiceSessionIssuer(IArcaConnectionReader reader, IArcaAccessTicketProvider tickets, TimeProvider timeProvider)
{
    public async Task<Guid?> FindConnectionIdAsync(CancellationToken cancellationToken) => (await reader.FindAsync(cancellationToken))?.Id;

    public async Task<Result<ArcaServiceSession>> OpenAsync(ArcaServiceCode service, CancellationToken cancellationToken)
    {
        if (await reader.FindSigningCertificateAsync(ArcaCertificateRole.Active, cancellationToken) is not { } certificate)
            return await reader.FindAsync(cancellationToken) is null ? ArcaConnectionErrors.NotConnected : ArcaConnectionErrors.CertificateNotLoaded;

        var outcome = await tickets.GetAsync(certificate.ToRef(), service, cancellationToken);
        return outcome is ArcaTicketOutcome.Ready ticket ? new ArcaServiceSession(certificate.ToRef(), ticket) : Failure(outcome);
    }

    /// <summary>El error de una respuesta de un servicio que no está lista.</summary>
    public static Error Failure(ArcaServiceCheckStatus status) => status switch
    {
        ArcaServiceCheckStatus.NotAuthorized => ArcaConnectionErrors.ServiceNotAuthorized,
        ArcaServiceCheckStatus.ClockSkew => ArcaConnectionErrors.ClockSkew,
        ArcaServiceCheckStatus.Unavailable => ArcaConnectionErrors.Unavailable,
        _ => ArcaConnectionErrors.ServiceRejected,
    };

    private Error Failure(ArcaTicketOutcome outcome) => outcome switch
    {
        ArcaTicketOutcome.Blocked blocked => ArcaConnectionErrors.ServiceBlocked(
            Math.Max(1, (int)Math.Ceiling((blocked.UntilUtc - timeProvider.GetUtcNow().UtcDateTime).TotalSeconds))),
        ArcaTicketOutcome.NotAuthorized => ArcaConnectionErrors.ServiceNotAuthorized,
        ArcaTicketOutcome.ClockSkew => ArcaConnectionErrors.ClockSkew,
        _ => ArcaConnectionErrors.Unavailable,
    };
}
