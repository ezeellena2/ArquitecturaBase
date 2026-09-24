using System.Collections;
using System.Globalization;
using System.Net;
using ArquitecturaBase.Infrastructure.Emails;
using ArquitecturaBase.Infrastructure.Emails.Resources;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class EmailTemplateRendererTests
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Spanish_email_starts_the_subject_with_the_code()
    {
        var message = Renderer().RenderLoginCode("ana@example.com", "482913", 10, Spanish);

        Assert.Equal("ana@example.com", message.To);
        Assert.Equal("482913 es tu código de acceso a Arquitectura Base", message.Subject);
        Assert.Contains("482913", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("lang=\"es\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Vence en 10 minutos.", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void English_email_uses_the_english_texts()
    {
        var message = Renderer().RenderLoginCode("ana@example.com", "482913", 10, English);

        Assert.Equal("482913 is your Arquitectura Base access code", message.Subject);
        Assert.Contains("It expires in 10 minutes.", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("lang=\"en\"", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void The_email_to_add_an_address_says_what_it_is_for_and_starts_the_subject_with_the_code()
    {
        var spanish = Renderer().RenderEmailVerificationCode("ana@example.com", "482913", 10, Spanish);
        var english = Renderer().RenderEmailVerificationCode("ana@example.com", "482913", 10, English);

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
    public void The_invitation_greets_by_the_name_and_takes_to_the_login()
    {
        var message = Renderer().RenderInvitation("laura.rios@example.com", "Laura", "https://app.test/login", Spanish);

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
    public void The_invitation_without_a_name_greets_without_one_and_goes_in_english_too()
    {
        var spanish = Renderer().RenderInvitation("ana@example.com", displayName: null, "https://app.test/login", Spanish);
        var english = Renderer().RenderInvitation("ana@example.com", "Laura", "https://app.test/login", English);

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
    public void The_name_in_the_invitation_is_html_encoded()
    {
        var message = Renderer().RenderInvitation("ana@example.com", "<b>Ana</b>", "https://app.test/login?a=1&b=2", Spanish);

        Assert.Contains("Hola, &lt;b&gt;Ana&lt;/b&gt;.", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("href=\"https://app.test/login?a=1&amp;b=2\"", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Ana</b>", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_are_html_encoded()
    {
        var message = Renderer(appName: "A&B <Test>").RenderLoginCode("ana@example.com", "482913", 10, English);

        Assert.Contains("A&amp;B &lt;Test&gt;", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<Test>", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Logo_is_shown_only_when_configured()
    {
        var withLogo = Renderer(logoUrl: "https://cdn.example.com/logo.png").RenderLoginCode("ana@example.com", "482913", 10, English);
        var withoutLogo = Renderer().RenderLoginCode("ana@example.com", "482913", 10, English);

        Assert.Contains("<img src=\"https://cdn.example.com/logo.png\"", withLogo.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", withoutLogo.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_texts_have_the_same_keys_in_spanish_and_english()
    {
        Assert.Equal(Keys(CultureInfo.InvariantCulture), Keys(English));
    }

    private static EmailTemplateRenderer Renderer(string appName = "Arquitectura Base", string? logoUrl = null) =>
        new(Options.Create(new EmailOptions { AppName = appName, LogoUrl = logoUrl }));

    private static string[] Keys(CultureInfo culture) =>
        EmailTexts.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
