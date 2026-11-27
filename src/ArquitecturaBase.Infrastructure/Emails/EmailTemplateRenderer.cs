using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Infrastructure.Emails.Resources;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Arma los emails con las plantillas embebidas: cada {{Marcador}} se reemplaza por un valor ya escapado.
/// Un marcador sin valor es un error de programación y lanza. El nombre de la aplicación y el logo son los de Configuración ›
/// Apariencia, leídos para cada correo (RC6-B); el logo es el archivo subido, servido por la dirección pública de la
/// instalación con su versión (ME2e).
/// </summary>
internal sealed partial class EmailTemplateRenderer(ISystemSettingsReader settings, IPublicOrigin publicOrigin) : IEmailTemplateRenderer
{
    private const string LayoutTemplate = "_Layout.html";
    private const string LoginCodeTemplate = "LoginCode.html";
    private const string InvitationTemplate = "Invitation.html";

    private static readonly ConcurrentDictionary<string, string> Templates = new(StringComparer.Ordinal);

    public async Task<EmailMessage> RenderLoginCodeAsync(string to, string code, int lifetimeMinutes, CultureInfo culture,
        CancellationToken cancellationToken) =>
        RenderCode(await settings.FindBrandingAsync(cancellationToken), "LoginCode", to, code, lifetimeMinutes, culture);

    public async Task<EmailMessage> RenderEmailVerificationCodeAsync(string to, string code, int lifetimeMinutes, CultureInfo culture,
        CancellationToken cancellationToken) =>
        RenderCode(await settings.FindBrandingAsync(cancellationToken), "VerifyEmail", to, code, lifetimeMinutes, culture);

    public async Task<EmailMessage> RenderPhoneUnlinkCodeAsync(string to, string code, int lifetimeMinutes, CultureInfo culture,
        CancellationToken cancellationToken) =>
        RenderCode(await settings.FindBrandingAsync(cancellationToken), "UnlinkPhone", to, code, lifetimeMinutes, culture);

    public async Task<EmailMessage> RenderPhoneLinkCodeAsync(string to, string code, int lifetimeMinutes, CultureInfo culture,
        CancellationToken cancellationToken) =>
        RenderCode(await settings.FindBrandingAsync(cancellationToken), "LinkPhone", to, code, lifetimeMinutes, culture);

    /// <summary>
    /// La invitación de un administrador (panel 3 del tablero de mensajes): el saludo con el nombre, si lo hay, un botón a
    /// /login y cómo se entra. No lleva código ni enlace para entrar: la persona entra con el código de siempre. El pie
    /// es el del tablero, que le dice qué hacer si no la esperaba, en lugar del aviso automático de los demás correos.
    /// </summary>
    public async Task<EmailMessage> RenderInvitationAsync(string to, string? displayName, string loginUrl, CultureInfo culture,
        CancellationToken cancellationToken)
    {
        var branding = await settings.FindBrandingAsync(cancellationToken);
        var appName = branding.AppName;
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
            ["Header"] = HeaderHtml(branding),
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
    private EmailMessage RenderCode(SystemBrandingResponse branding, string textsPrefix, string to, string code, int lifetimeMinutes,
        CultureInfo culture)
    {
        var appName = branding.AppName;
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
            ["Header"] = HeaderHtml(branding),
            ["Content"] = content,
            ["Footer"] = Encode(footer),
        });

        var paragraphBreak = Environment.NewLine + Environment.NewLine;
        var text = string.Join(paragraphBreak, title, intro, code, expiry, footer);

        return new EmailMessage(to, EmailTexts.Format(textsPrefix + ".Subject", culture, code, appName), html, text);
    }

    // Sin logo, el encabezado lleva el nombre. El logo es PNG o JPG (Gmail no muestra SVG) y va con su versión: un correo
    // nuevo nunca muestra el logo anterior desde una caché.
    private string HeaderHtml(SystemBrandingResponse branding) =>
        branding.LogoVersion is not { } version || publicOrigin.Value is not { } origin
            ? Encode(branding.AppName)
            : $"<img src=\"{Encode(LogoAddress(origin, version))}\" alt=\"{Encode(branding.AppName)}\" height=\"32\" style=\"display:block;border:0;height:32px;\">";

    internal static string LogoAddress(Uri origin, string version) =>
        new Uri(origin, "api/settings/brand/logo?v=" + Uri.EscapeDataString(version)).AbsoluteUri;

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
