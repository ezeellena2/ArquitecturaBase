namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Connection;

/// <summary>El certificado que bajó la persona de ARCA, como texto PEM.</summary>
public sealed record UploadArcaCertificateHttpRequest(string Certificate, long ExpectedRevision)
{
    public override string ToString() => "UploadArcaCertificateHttpRequest { certificate: [REDACTED] }";
}
