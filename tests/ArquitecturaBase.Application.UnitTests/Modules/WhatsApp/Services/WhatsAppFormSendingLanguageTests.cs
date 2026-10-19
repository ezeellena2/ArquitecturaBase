using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFormSendingLanguageTests
{
    [Theory]
    [InlineData("es", "en", "Abrir formulario")]
    [InlineData("en", "es", "Open form")]
    public async Task Ordinary_sending_resolves_the_owners_original_culture_before_requiring_a_recipient_language_button(
        string original, string secondary, string expectedButton)
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = new WhatsAppAssistantSuggestionFixture();
        await fixture.Context.Users.UpdateProfileAsync(fixture.Context.Subject.Id, "Fictitious recipient", secondary,
            "America/Argentina/Buenos_Aires", ct);
        var texts = new Dictionary<string, string> { [original] = "Original" };
        var form = new StructuredFormSelectionRow(Guid.NewGuid(), Guid.NewGuid(), 1, "FORM", texts, 0, texts);
        fixture.Forms.Prefill = new StructuredFormPrefillResponse(form, 1, [], [], "Selection reason", original);
        fixture.Forms.Preparation = new StructuredFormPreparationResponse([], []) { Culture = original };
        var recipient = (await fixture.Context.Recipients.FindAsync(fixture.Context.Contact.Id, ct))!;
        var request = new SendWhatsAppFormRequest(fixture.Context.Contact.Id, Guid.NewGuid(), form.DefinitionId,
            form.VersionId, fixture.Context.Contact.ModeRevision,
            WhatsAppFormRecipientRevision.Create(fixture.Context.Operator.Id, recipient), null);

        var resolved = await WhatsAppAssistantAcceptanceFixture.CreateSending(fixture).ResolveAsync(request, fixture.Context.Operator.Id, ct);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(1, fixture.Forms.PreparationReads);
        Assert.Equal(secondary, resolved.Value.Recipient.Culture);
        Assert.Equal(original, resolved.Value.Binding.Culture);
        Assert.Equal(original, resolved.Value.Projection.Culture);
        Assert.Equal(expectedButton, resolved.Value.ButtonText);
        Assert.False(form.SubmitLabel.ContainsKey(secondary));
        Assert.Empty(fixture.Sessions.Rows);
        Assert.Empty(fixture.Conversation.Outgoing);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }
}
