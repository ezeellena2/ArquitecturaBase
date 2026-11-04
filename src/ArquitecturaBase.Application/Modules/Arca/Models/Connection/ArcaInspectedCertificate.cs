namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>Un certificado aceptado: el PEM sin texto alrededor, su vigencia y su huella SHA-256.</summary>
public sealed record ArcaInspectedCertificate(string CertificatePem, DateTime NotBeforeUtc, DateTime NotAfterUtc, string Thumbprint);
