using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>Lo que una familia recibe para probar sus servicios: el certificado activo y el proveedor de tickets.</summary>
public sealed record ArcaServiceContext(ArcaCertificateRef Certificate, IArcaAccessTicketProvider Tickets);
