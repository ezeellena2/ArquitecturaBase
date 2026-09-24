using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Infrastructure.Emails.Resources;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Arma los emails con las plantillas embebidas: cada {{Marcador}} se reemplaza por un valor ya escapado.
/// Un marcador sin valor es un error de programación y lanza.
/// </summary>
internal sealed partial class EmailTemplateRenderer(IOptions<EmailOptions> options) : IEmailTemplateRenderer
{
    private const string LayoutTemplate = "_Layout.html";
    private const string LoginCodeTemplate = "LoginCode.html";
    private const string InvitationTemplate = "Invitation.html";

    private static readonly ConcurrentDictionary<string, string> Templates = new(StringComparer.Ordinal);

    public EmailMessage RenderLoginCode(string to, string code, int lifetimeMinutes, CultureInfo culture) =>
        RenderCode("LoginCode", to, code, lifetimeMinutes, culture);

    public EmailMessage RenderEmailVerificationCode(string to, string code, int lifetimeMinutes, CultureInfo culture) =>
        RenderCode("VerifyEmail", to, code, lifetimeMinutes, culture);

    /// <summary>
    /// La invitación de un administrador (panel 3 del tablero de mensajes): el saludo con el nombre, si lo hay, un botón a
    /// /login y cómo se entra. No lleva código ni enlace para entrar: la persona entra con el código de siempre. El pie
    /// es el del tablero, que le dice qué hacer si no la esperaba, en lugar del aviso automático de los demás correos.
    /// </summary>
    public EmailMessage RenderInvitation(string to, string? displayName, string loginUrl, CultureInfo culture)
    {
        var appName = options.Value.AppName;
        var title = EmailTexts.Get("Invitation.Title", culture);
        var greeting = string.IsNullOrWhiteSpace(displayName)
            ? EmailTexts.Format("Invitation.GreetingWithoutName", culture, appName)
            : EmailTexts.Format("Invitation.Greeting", culture, displayName.Trim(), appName);
        var button = EmailTexts.Get("Invitation.Button", culture);
        var hint = EmailTexts.Get("Invitation.Hint", culture);
        var footer = EmailTexts.Get("Invitation.Footer", culture);

        var content = Fill(InvitationTemplate, new Dictionary<string, string>
        {
            ["Title"] = Encode(title),
            ["Greeting"] = Encode(greeting),
            ["LoginUrl"] = Encode(loginUrl),
            ["Button"] = Encode(button),
            ["Hint"] = Encode(hint),
        });

        var html = Fill(LayoutTemplate, new Dictionary<string, string>
        {
            ["Lang"] = Encode(culture.TwoLetterISOLanguageName),
            ["Title"] = Encode(title),
            ["Header"] = HeaderHtml(),
            ["Content"] = content,
            ["Footer"] = Encode(footer),
        });

        var paragraphBreak = Environment.NewLine + Environment.NewLine;
        var text = string.Join(paragraphBreak, title, greeting, $"{button}: {loginUrl}", hint, footer);

        return new EmailMessage(to, EmailTexts.Format("Invitation.Subject", culture, appName), html, text);
    }

    /// <summary>
    /// Los dos correos con un código comparten la plantilla y cambian los textos, que en Emails.resx llevan el prefijo
    /// <paramref name="textsPrefix"/>: Subject, Title, Intro y Expiry.
    /// </summary>
    private EmailMessage RenderCode(string textsPrefix, string to, string code, int lifetimeMinutes, CultureInfo culture)
    {
        var appName = options.Value.AppName;
        var title = EmailTexts.Get(textsPrefix + ".Title", culture);
        var intro = EmailTexts.Format(textsPrefix + ".Intro", culture, appName);
        var expiry = EmailTexts.Format(textsPrefix + ".Expiry", culture, lifetimeMinutes);
        var footer = EmailTexts.Format("Layout.Footer", culture, appName);

        var content = Fill(LoginCodeTemplate, new Dictionary<string, string>
        {
            ["Title"] = Encode(title),
            ["Intro"] = Encode(intro),
            ["Code"] = Encode(code),
            ["Expiry"] = Encode(expiry),
        });

        var html = Fill(LayoutTemplate, new Dictionary<string, string>
        {
            ["Lang"] = Encode(culture.TwoLetterISOLanguageName),
            ["Title"] = Encode(title),
            ["Header"] = HeaderHtml(),
            ["Content"] = content,
            ["Footer"] = Encode(footer),
        });

        var paragraphBreak = Environment.NewLine + Environment.NewLine;
        var text = string.Join(paragraphBreak, title, intro, code, expiry, footer);

        return new EmailMessage(to, EmailTexts.Format(textsPrefix + ".Subject", culture, code, appName), html, text);
    }

    private string HeaderHtml()
    {
        var settings = options.Value;

        return string.IsNullOrWhiteSpace(settings.LogoUrl)
            ? Encode(settings.AppName)
            : $"<img src=\"{Encode(settings.LogoUrl)}\" alt=\"{Encode(settings.AppName)}\" height=\"32\" style=\"display:block;border:0;height:32px;\">";
    }

    private static string Fill(string templateName, Dictionary<string, string> values) =>
        PlaceholderPattern().Replace(Load(templateName), match =>
            values.TryGetValue(match.Groups["name"].Value, out var value)
                ? value
                : throw new InvalidOperationException($"Template '{templateName}' has no value for '{match.Value}'."));

    private static string Load(string templateName) =>
        Templates.GetOrAdd(templateName, static name =>
        {
            using var stream = typeof(EmailTemplateRenderer).Assembly.GetManifestResourceStream("EmailTemplates." + name)
                ?? throw new InvalidOperationException($"Missing email template '{name}'.");
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        });

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex(@"\{\{(?<name>[A-Za-z]+)\}\}")]
    private static partial Regex PlaceholderPattern();
}
