using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>
/// ADR 0021, point 5: the server checks a drafted reply before it is queued. Short, in the language of the conversation, and
/// without links, phones, emails or prices that are not in what the business loaded. What does not pass never goes out.
/// </summary>
public sealed class WhatsAppDraftedReplyPolicyTests
{
    private static readonly string[] Knowledge =
    [
        "Sí: al interior enviamos por transporte, a cargo del cliente. En CABA y GBA entregamos nosotros.",
        "Atención al 011 4555-1234 de lunes a viernes.",
        "Catálogo en www.arquitecturabase.com.ar/catalogo",
        "La visita técnica cuesta $15.000.",
    ];

    [Theory]
    [InlineData("¡Hola! Soy el asistente de Arquitectura Base. ¿En qué te puedo ayudar?")]
    [InlineData("Son las 10:42.")]
    [InlineData("Sí: al interior enviamos por transporte, a cargo del cliente.")]
    [InlineData("Podés llamarnos al 011 4555-1234.")]
    [InlineData("Mirá el catálogo en www.arquitecturabase.com.ar/catalogo.")]
    [InlineData("La visita técnica cuesta $15.000.")]
    public void A_reply_with_only_what_was_loaded_passes(string text)
    {
        Assert.Empty(WhatsAppDraftedReplyPolicy.Problems(text, "es", 400, Knowledge));
    }

    [Theory]
    [InlineData("Escribinos a ventas@example.com.", "email")]
    [InlineData("Mirá https://otra-pagina.com/ofertas.", "link")]
    [InlineData("Entrá a promo.example.net y elegí.", "link")]
    [InlineData("Llamá al +54 9 11 5555-0000.", "phone")]
    [InlineData("El envío cuesta $2.500.", "price")]
    [InlineData("Sale 3000 pesos.", "price")]
    public void A_link_phone_email_or_price_that_was_not_loaded_does_not_pass(string text, string problem)
    {
        Assert.Contains(problem, WhatsAppDraftedReplyPolicy.Problems(text, "es", 400, Knowledge));
    }

    [Fact]
    public void A_reply_longer_than_the_limit_or_empty_does_not_pass()
    {
        Assert.Contains("tooLong", WhatsAppDraftedReplyPolicy.Problems(new string('a', 401), "es", 400, Knowledge));
        Assert.Contains("empty", WhatsAppDraftedReplyPolicy.Problems("   ", "es", 400, Knowledge));
    }

    [Fact]
    public void A_reply_in_the_other_language_does_not_pass()
    {
        Assert.Contains("language", WhatsAppDraftedReplyPolicy.Problems("Yes, we ship to other provinces and you pay for the carrier.", "es", 400, Knowledge));
        Assert.Contains("language", WhatsAppDraftedReplyPolicy.Problems("Sí, enviamos al interior por transporte y el envío lo paga el cliente.", "en", 400, Knowledge));
        Assert.Contains("language", WhatsAppDraftedReplyPolicy.Problems("¿Querés que te pase con alguien?", "en", 400, Knowledge));
        Assert.Empty(WhatsAppDraftedReplyPolicy.Problems("Yes, we ship to other provinces by carrier, paid by the customer.", "en", 400, Knowledge));
        Assert.Empty(WhatsAppDraftedReplyPolicy.Problems("OK.", "en", 400, Knowledge));
    }
}
