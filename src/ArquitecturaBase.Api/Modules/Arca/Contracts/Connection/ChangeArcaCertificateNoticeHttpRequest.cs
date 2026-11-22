namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Connection;

/// <summary>RC7-B: «Avisar N días antes» del vencimiento del certificado, con la revisión de la conexión que se leyó.</summary>
public sealed record ChangeArcaCertificateNoticeHttpRequest(int Days, long ExpectedRevision);
