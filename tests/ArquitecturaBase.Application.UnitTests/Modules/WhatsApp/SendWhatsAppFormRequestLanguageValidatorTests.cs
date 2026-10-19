using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class SendWhatsAppFormRequestLanguageValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Completá la visita cuando puedas.")]
    [InlineData("Please complete the visit.\nThank you.")]
    public async Task A_null_message_selects_the_server_opening_and_a_visible_custom_message_is_kept(string? message)
    {
        var request = Request(message);

        var result = await new SendWhatsAppFormRequestValidator().ValidateAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(message, request.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u200b")]
    [InlineData("\u200c\u200d")]
    public async Task An_explicit_empty_blank_or_invisible_opening_is_still_invalid(string message)
    {
        var result = await new SendWhatsAppFormRequestValidator().ValidateAsync(Request(message), TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Message");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task Custom_openings_keep_the_existing_interactive_body_limit(int extraLength, bool valid)
    {
        var message = new string('x', WhatsAppOutboundMessage.MaxInteractiveBodyLength + extraLength);

        var result = await new SendWhatsAppFormRequestValidator().ValidateAsync(Request(message), TestContext.Current.CancellationToken);

        Assert.Equal(valid, result.IsValid);
    }

    private static SendWhatsAppFormRequest Request(string? message) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, new string('a', 64), message);
}
