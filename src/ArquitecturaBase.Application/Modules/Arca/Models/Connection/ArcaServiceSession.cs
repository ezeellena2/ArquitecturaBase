namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>Lo que una familia necesita para llamar a un servicio: el certificado activo y su ticket listo.</summary>
public sealed record ArcaServiceSession(ArcaCertificateRef Certificate, ArcaTicketOutcome.Ready Ticket);
