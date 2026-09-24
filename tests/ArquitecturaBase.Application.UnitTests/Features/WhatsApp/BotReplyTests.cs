using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Features.WhatsApp.HandleInboundMessage;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.Features.WhatsApp;

/// <summary>
/// Las respuestas del bot en los dos idiomas. Meta pone límites (20 caracteres el título de un botón, 60 el pie y 1024
/// el cuerpo) y los mensajes los controlan al armarse: armar todas las respuestas en los dos idiomas prueba que ningún
/// texto de Bot.resx se pasa.
/// </summary>
public sealed class BotReplyTests
{
    private static readonly PhoneNumber Phone = PhoneNumber.Create("+5493413654813").Value;

    public static TheoryData<string> Cultures => new() { "es", "en" };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Every_reply_fits_the_limits_of_meta(string culture)
    {
        var reply = BotReply.For("Arquitectura Base", AccountIn(culture));
        var longName = new string('A', 256);

        WhatsAppOutboundMessage[] messages =
        [
            reply.SignIn(Phone, longName, "https://app.test/ingresar#t=token"),
            reply.SignIn(Phone, name: null, "https://app.test/ingresar#t=token"),
            reply.AccountCreated(Phone, longName, "https://app.test/ingresar#t=token"),
            reply.AccountCreated(Phone, name: null, "https://app.test/ingresar#t=token"),
            reply.NoAccount(Phone),
            reply.HaveAccount(Phone, "https://app.test/login"),
            reply.NotInvited(Phone, "https://app.test/login"),
            reply.Disabled(Phone),
            reply.TooManyLinks(Phone),
        ];

        Assert.All(messages, message => Assert.Equal(Phone, message.To));
    }

    /// <summary>Sin cuenta, o con un idioma que el bot no habla, en español.</summary>
    [Fact]
    public void Without_an_account_or_with_an_unknown_language_it_speaks_spanish()
    {
        const string Spanish = "Tu cuenta está deshabilitada. Contactá a un administrador.";

        Assert.Equal(Spanish, BotReply.For("Arquitectura Base", account: null).Disabled(Phone).Body);
        Assert.Equal(Spanish, BotReply.For("Arquitectura Base", AccountIn("fr")).Disabled(Phone).Body);
    }

    /// <summary>El historial guarda el resumen seguro: nunca la URL del enlace, que lleva el token.</summary>
    [Fact]
    public void The_safe_summary_of_the_sign_in_link_leaves_the_url_out()
    {
        var message = BotReply.For("Arquitectura Base", account: null).SignIn(Phone, "Ana", "https://app.test/ingresar#t=secret-token");

        Assert.DoesNotContain("secret-token", message.SafeSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", message.SafeSummary, StringComparison.Ordinal);
    }

    private static UserAccount AccountIn(string culture) =>
        new(Guid.CreateVersion7(), Email: null, EmailConfirmed: false, Phone.Value, PhoneNumberConfirmed: true, "Ana", culture, "UTC", IsActive: true);
}
