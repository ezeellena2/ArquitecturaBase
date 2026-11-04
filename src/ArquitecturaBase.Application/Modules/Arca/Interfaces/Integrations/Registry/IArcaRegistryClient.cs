using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;

/// <summary>La constancia de inscripción del padrón (<c>ws_sr_constancia_inscripcion</c>).</summary>
public interface IArcaRegistryClient
{
    /// <summary><c>getPersona_v2</c> de <paramref name="cuit"/>, consultada en nombre de la CUIT del certificado.</summary>
    Task<ArcaServiceReply<ArcaRegistryPerson>> FindPersonAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket, string cuit,
        CancellationToken cancellationToken);
}
