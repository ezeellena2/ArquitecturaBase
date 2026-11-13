using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// Each rule of a form request answers with a text of the project, never with the one FluentValidation brings (in Spanish
// from Spain and with the name of the property in English). The errors are the ones the person gets, one per field.
public sealed class WhatsAppFormRequestValidationMessageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listing_the_forms_of_a_chat_without_the_chat_says_it_is_required()
    {
        using var culture = new CultureScope("es");

        var error = await RequestValidator.ValidateAsync(new ListWhatsAppFormsRequest(Guid.Empty), [new ListWhatsAppFormsRequestValidator()], Ct);

        Assert.Equal(new Dictionary<string, string[]> { ["contactId"] = ["Este campo es obligatorio."] }, error?.Errors);
    }

    [Theory]
    [InlineData("es", "Este campo es obligatorio.")]
    [InlineData("en", "This field is required.")]
    public async Task A_send_with_empty_values_says_each_is_required(string language, string required)
    {
        using var culture = new CultureScope(language);

        var error = await RequestValidator.ValidateAsync(
            new SendWhatsAppFormRequest(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, -1, null, ""), [new SendWhatsAppFormRequestValidator()], Ct);

        Assert.Equal(new Dictionary<string, string[]>
        {
            ["contactId"] = [required],
            ["requestId"] = [required],
            ["definitionId"] = [required],
            ["versionId"] = [required],
            ["expectedModeRevision"] = [required],
            ["expectedRecipientContextRevision"] = [required],
            ["message"] = [required],
        }, error?.Errors);
    }

    [Theory]
    [InlineData("es", "Ingresá como máximo 67 caracteres.", "Ingresá como máximo 1.024 caracteres.")]
    [InlineData("en", "Enter at most 67 characters.", "Enter at most 1,024 characters.")]
    public async Task A_send_with_values_too_long_says_each_maximum(string language, string revision, string message)
    {
        using var culture = new CultureScope(language);
        var request = new SendWhatsAppFormRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0,
            new string('a', 68), new string('x', WhatsAppOutboundMessage.MaxInteractiveBodyLength + 1));

        var error = await RequestValidator.ValidateAsync(request, [new SendWhatsAppFormRequestValidator()], Ct);

        Assert.Equal(new Dictionary<string, string[]>
        {
            ["expectedRecipientContextRevision"] = [revision],
            ["message"] = [message],
        }, error?.Errors);
    }
}
