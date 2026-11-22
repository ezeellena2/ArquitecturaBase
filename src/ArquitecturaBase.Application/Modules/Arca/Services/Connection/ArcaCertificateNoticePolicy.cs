using System.Globalization;
using System.Net;
using System.Resources;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// RC7-B: el correo del aviso de vencimiento del certificado, en el idioma de cada persona y con la fecha sólo con números en
/// su zona (dd/MM/yyyy en español, MM/dd/yyyy en inglés), como la pantalla. Sus textos están en <c>Resources/CertificateNotice</c>.
/// </summary>
internal static class ArcaCertificateNoticePolicy
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Modules.Arca.Resources.CertificateNotice", typeof(ArcaCertificateNoticePolicy).Assembly);

    public static EmailMessage Compose(ArcaNoticeRecipientRow recipient, DateTime notAfterUtc)
    {
        var date = Date(notAfterUtc, recipient.TimeZoneId, recipient.Culture);
        var subject = DisplayNumbers.Format(recipient.Culture, Text("Notice.Subject", recipient.Culture), date);
        var body = DisplayNumbers.Format(recipient.Culture, Text("Notice.Body", recipient.Culture), date);
        return new EmailMessage(recipient.Email, subject, $"<p>{WebUtility.HtmlEncode(body)}</p>", body);
    }

    private static string Date(DateTime atUtc, string timeZoneId, string culture)
    {
        var local = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) ? TimeZoneInfo.ConvertTimeFromUtc(atUtc, zone) : atUtc;
        return local.ToString(IsEnglish(culture) ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private static bool IsEnglish(string culture) => culture.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    private static string Text(string key, string culture) => ResourceManager.GetString(key, DisplayNumbers.Culture(culture))
        ?? throw new InvalidOperationException("The ARCA certificate notice text is missing.");
}
