namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>El pedido de certificado pendiente para descargar, con el alias que lleva el nombre del archivo.</summary>
public sealed record ArcaPendingRequestResponse(string Alias, string RequestPem);
