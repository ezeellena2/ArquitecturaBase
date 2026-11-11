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

    /// <summary>Una sesión para operar en nombre de la empresa: en modo delegación, el ticket tiene que incluirla (ADR 0027).</summary>
    public Task<Result<ArcaServiceSession>> OpenAsync(ArcaServiceCode service, CancellationToken cancellationToken) =>
        OpenAsync(service, onBehalfOfCompany: true, cancellationToken);

    /// <summary>
    /// Una sesión en nombre de quien firma, para una consulta que no representa a la empresa, como la constancia de un tercero:
    /// en modo delegación la hace la plataforma con su CUIT, sin delegarla (DG4). En el modo directo es la misma sesión.
    /// </summary>
    public Task<Result<ArcaServiceSession>> OpenAsSignerAsync(ArcaServiceCode service, CancellationToken cancellationToken) =>
        OpenAsync(service, onBehalfOfCompany: false, cancellationToken);

    private async Task<Result<ArcaServiceSession>> OpenAsync(ArcaServiceCode service, bool onBehalfOfCompany, CancellationToken cancellationToken)
    {
        if (await reader.FindSigningCertificateAsync(ArcaCertificateRole.Active, cancellationToken) is not { } certificate)
            return await reader.FindAsync(cancellationToken) is null ? ArcaConnectionErrors.NotConnected : ArcaConnectionErrors.CertificateNotLoaded;

        var reference = onBehalfOfCompany ? certificate.ToRef(new HashSet<string>(StringComparer.Ordinal) { service.Value }) : certificate.ToRef();
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
