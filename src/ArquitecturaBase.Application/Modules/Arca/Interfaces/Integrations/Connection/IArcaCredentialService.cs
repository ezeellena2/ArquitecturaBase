using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;

/// <summary>
/// La criptografía de la conexión con el anillo del módulo (ADR 0023). Cada secreto se cifra atado a su conexión, su
/// certificado y su propósito: la clave privada no sale nunca en claro y el ticket no sirve para otro servicio.
/// </summary>
public interface IArcaCredentialService
{
    /// <summary>Genera la clave y el pedido de certificado; quien llama ya validó el alias.</summary>
    ArcaCertificateRequest CreateRequest(Guid connectionId, Guid certificateId, string cuit, string alias);

    /// <summary>
    /// Revisa el certificado que bajó la persona: que sea del ambiente, de esa clave y de esa CUIT, y que no esté vencido.
    /// Lanza si la clave guardada no se descifra con el anillo, porque eso no lo resuelve la persona.
    /// </summary>
    Result<ArcaInspectedCertificate> Inspect(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem,
        string cuit, ArcaEnvironment environment, DateTime nowUtc);

    /// <summary>Firma el pedido de ticket del WSAA con la clave y el certificado, y devuelve el CMS en base64.</summary>
    string SignLoginRequest(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem, byte[] loginTicketRequest);

    string ProtectTicket(Guid connectionId, Guid certificateId, ArcaServiceCode service, string token, string sign);

    /// <summary>Null si el texto no es de ese certificado y servicio, o si su clave ya no está en el anillo.</summary>
    (string Token, string Sign)? UnprotectTicket(Guid connectionId, Guid certificateId, ArcaServiceCode service, string protectedCredentials);
}
