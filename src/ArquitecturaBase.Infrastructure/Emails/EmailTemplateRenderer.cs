using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Abstractions.Emails;
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

    private static readonly ConcurrentDictionary<string, string> Templates = new(StringComparer.Ordinal);

    public EmailMessage RenderLoginCode(string to, string code, int lifetimeMinutes, CultureInfo culture)
    {
        var appName = options.Value.AppName;
        var title = EmailTexts.Get("LoginCode.Title", culture);
        var intro = EmailTexts.Format("LoginCode.Intro", culture, appName);
        var expiry = EmailTexts.Format("LoginCode.Expiry", culture, lifetimeMinutes);
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

        return new EmailMessage(to, EmailTexts.Format("LoginCode.Subject", culture, code, appName), html, text);
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
