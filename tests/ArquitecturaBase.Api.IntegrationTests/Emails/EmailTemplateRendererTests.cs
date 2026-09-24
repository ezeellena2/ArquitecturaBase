using System.Collections;
using System.Globalization;
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
