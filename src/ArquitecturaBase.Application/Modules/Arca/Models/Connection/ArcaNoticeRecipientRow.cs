namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// RC7-B: una persona a la que le llega el aviso de vencimiento del certificado, con su idioma y su zona para escribir la fecha.
/// El texto no muestra el correo.
/// </summary>
public sealed record ArcaNoticeRecipientRow(string Email, string Culture, string TimeZoneId)
{
    public override string ToString() => $"ArcaNoticeRecipientRow {{ email: [REDACTED], culture: {Culture} }}";
}
