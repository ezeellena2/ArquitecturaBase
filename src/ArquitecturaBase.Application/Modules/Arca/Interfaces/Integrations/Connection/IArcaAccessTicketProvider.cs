using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;

/// <summary>
/// El ticket del WSAA de un certificado para un servicio, compartido por todos los hosts (ADR 0023). Pide uno nuevo sólo
/// cuando el guardado vence dentro del margen, y nunca lo pide adentro de una transacción.
/// </summary>
public interface IArcaAccessTicketProvider
{
    Task<ArcaTicketOutcome> GetAsync(ArcaCertificateRef certificate, ArcaServiceCode service, CancellationToken cancellationToken);
}
