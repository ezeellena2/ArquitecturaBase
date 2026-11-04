namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>El pedido de certificado pendiente, que es público: se descarga para crear el certificado en ARCA.</summary>
public sealed record ArcaPendingRequestRow(string Alias, string RequestPem);
