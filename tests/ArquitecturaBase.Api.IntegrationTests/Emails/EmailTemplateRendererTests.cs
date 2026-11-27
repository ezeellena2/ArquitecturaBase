using System.Collections;
using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Infrastructure.Emails;
using ArquitecturaBase.Infrastructure.Emails.Resources;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class EmailTemplateRendererTests
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    [Fact]
    public async Task Spanish_email_starts_the_subject_with_the_code()
    {
        var message = await Renderer().RenderLoginCodeAsync("ana@example.com", "482913", 10, Spanish, Ct);

        Assert.Equal("ana@example.com", message.To);
        Assert.Equal("482913 es tu código de acceso a Arquitectura Base", message.Subject);
        Assert.Contains("482913", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("lang=\"es\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Vence en 10 minutos.", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task English_email_uses_the_english_texts()
    {
        var message = await Renderer().RenderLoginCodeAsync("ana@example.com", "482913", 10, English, Ct);

        Assert.Equal("482913 is your Arquitectura Base access code", message.Subject);
        Assert.Contains("It expires in 10 minutes.", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("lang=\"en\"", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_email_to_add_an_address_says_what_it_is_for_and_starts_the_subject_with_the_code()
    {
        var spanish = await Renderer().RenderEmailVerificationCodeAsync("ana@example.com", "482913", 10, Spanish, Ct);
        var english = await Renderer().RenderEmailVerificationCodeAsync("ana@example.com", "482913", 10, English, Ct);

        Assert.Equal("ana@example.com", spanish.To);
        Assert.Equal("482913 es tu código para agregar este correo a Arquitectura Base", spanish.Subject);
        Assert.Contains("Usá este código para agregar este correo a tu cuenta de Arquitectura Base:", spanish.TextBody, StringComparison.Ordinal);
        Assert.Contains("Vence en 10 minutos.", spanish.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("482913", spanish.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", spanish.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ingresar", spanish.TextBody, StringComparison.Ordinal);

        Assert.Equal("482913 is your code to add this email to Arquitectura Base", english.Subject);
        Assert.Contains("It expires in 10 minutes.", english.TextBody, StringComparison.Ordinal);
        Assert.Contains("lang=\"en\"", english.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>Los textos del panel 3 del tablero de mensajes, texto por texto. No lleva código ni enlace para entrar.</summary>
    [Fact]
    public async Task The_invitation_greets_by_the_name_and_takes_to_the_login()
    {
        var message = await Renderer().RenderInvitationAsync("laura.rios@example.com", "Laura", "https://app.test/login", Spanish, Ct);

        // Los acentos van como entidades en el HTML: los textos se comparan con el HTML ya leído.
        var html = WebUtility.HtmlDecode(message.HtmlBody);
        Assert.Equal("laura.rios@example.com", message.To);
        Assert.Equal("Te dieron acceso a Arquitectura Base", message.Subject);
        Assert.Contains("<title>Te dieron acceso</title>", html, StringComparison.Ordinal);
        Assert.Contains("Hola, Laura. Un administrador te dio acceso a Arquitectura Base.", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://app.test/login\"", html, StringComparison.Ordinal);
        Assert.Contains(">Ingresar</a>", html, StringComparison.Ordinal);
        Assert.Contains("Para entrar, usá este correo: te vamos a mandar un código de acceso.", html, StringComparison.Ordinal);
        Assert.Contains(
            "Si no esperabas este correo, podés ignorarlo. Sin el código, nadie puede entrar a tu cuenta.",
            html,
            StringComparison.Ordinal);
        Assert.Contains("lang=\"es\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", message.HtmlBody, StringComparison.Ordinal);

        Assert.Equal(
            string.Join(
                Environment.NewLine + Environment.NewLine,
                "Te dieron acceso",
                "Hola, Laura. Un administrador te dio acceso a Arquitectura Base.",
                "Ingresar: https://app.test/login",
                "Para entrar, usá este correo: te vamos a mandar un código de acceso.",
                "Si no esperabas este correo, podés ignorarlo. Sin el código, nadie puede entrar a tu cuenta."),
            message.TextBody);
    }

    [Fact]
    public async Task The_invitation_without_a_name_greets_without_one_and_goes_in_english_too()
    {
        var spanish = await Renderer().RenderInvitationAsync("ana@example.com", displayName: null, "https://app.test/login", Spanish, Ct);
        var english = await Renderer().RenderInvitationAsync("ana@example.com", "Laura", "https://app.test/login", English, Ct);

        Assert.Contains("Hola. Un administrador te dio acceso a Arquitectura Base.", spanish.TextBody, StringComparison.Ordinal);
        Assert.Equal("You've been given access to Arquitectura Base", english.Subject);
        Assert.Contains("Hi, Laura. An administrator gave you access to Arquitectura Base.", english.TextBody, StringComparison.Ordinal);
        Assert.Contains("Sign in: https://app.test/login", english.TextBody, StringComparison.Ordinal);
        Assert.Contains("To sign in, use this email: we'll send you an access code.", english.TextBody, StringComparison.Ordinal);
        Assert.Contains(
            "If you weren't expecting this email, you can ignore it. Without the code, nobody can access your account.",
            english.TextBody,
            StringComparison.Ordinal);
        Assert.Contains("lang=\"en\"", english.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_name_in_the_invitation_is_html_encoded()
    {
        var message = await Renderer().RenderInvitationAsync("ana@example.com", "<b>Ana</b>", "https://app.test/login?a=1&b=2", Spanish, Ct);

        Assert.Contains("Hola, &lt;b&gt;Ana&lt;/b&gt;.", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("href=\"https://app.test/login?a=1&amp;b=2\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Ana</b>", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Values_are_html_encoded()
    {
        var message = await Renderer(appName: "A&B <Test>").RenderLoginCodeAsync("ana@example.com", "482913", 10, English, Ct);

        Assert.Contains("A&amp;B &lt;Test&gt;", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<Test>", message.HtmlBody, StringComparison.Ordinal);
    }

    // ME2e: the uploaded logo, served by the installation's public address with its version.
    [Fact]
    public async Task Logo_is_shown_only_when_uploaded()
    {
        var withLogo = await Renderer(logoVersion: "0193a4c27d1e7c3a9f001a2b3c4d5e6f").RenderLoginCodeAsync("ana@example.com", "482913", 10, English, Ct);
        var withoutLogo = await Renderer().RenderLoginCodeAsync("ana@example.com", "482913", 10, English, Ct);

        Assert.Contains("<img src=\"https://app.test/api/settings/brand/logo?v=0193a4c27d1e7c3a9f001a2b3c4d5e6f\"", withLogo.HtmlBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<img", withoutLogo.HtmlBody, StringComparison.Ordinal);
    }

    // RC6-B: the name and the logo come from Configuración › Apariencia and are read for each email, so a change counts for the next one.
    [Fact]
    public async Task The_name_and_the_logo_saved_in_appearance_are_read_for_each_email()
    {
        var settings = FixedBrandingReader.Of("Campo", "Campo Avisos", "0193a4c27d1e7c3a9f001a2b3c4d5e6f");
        var renderer = new EmailTemplateRenderer(settings, Origin);

        var code = await renderer.RenderLoginCodeAsync("ana@example.com", "482913", 10, Spanish, Ct);
        var invitation = await renderer.RenderInvitationAsync("ana@example.com", "Ana", "https://app.test/login", Spanish, Ct);

        Assert.Equal("482913 es tu código de acceso a Campo", code.Subject);
        Assert.Contains("<img src=\"https://app.test/api/settings/brand/logo?v=0193a4c27d1e7c3a9f001a2b3c4d5e6f\" alt=\"Campo\"",
            code.HtmlBody, StringComparison.Ordinal);
        Assert.Equal("Te dieron acceso a Campo", invitation.Subject);
        Assert.Equal(2, settings.Reads);
    }

    [Fact]
    public void Email_texts_have_the_same_keys_in_spanish_and_english()
    {
        Assert.Equal(Keys(CultureInfo.InvariantCulture), Keys(English));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EmailTemplateRenderer Renderer(string appName = "Arquitectura Base", string? logoVersion = null) =>
        new(FixedBrandingReader.Of(appName, logoVersion: logoVersion), Origin);

    private static FixedPublicOrigin Origin { get; } = new(new Uri("https://app.test/"));

    private sealed record FixedPublicOrigin(Uri? Value) : IPublicOrigin;

    private static string[] Keys(CultureInfo culture) =>
        EmailTexts.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
