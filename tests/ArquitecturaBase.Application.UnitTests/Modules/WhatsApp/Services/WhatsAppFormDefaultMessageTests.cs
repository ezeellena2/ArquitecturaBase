using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// The message that goes with a form names it, in the language the form goes out in (2026-11-07).
public sealed class WhatsAppFormDefaultMessageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es", true, "Completá el formulario «Prueba renglones 1».")]
    [InlineData("en", true, "Complete the form “Rows test 1”.")]
    [InlineData("en", false, "Completá el formulario «Prueba renglones 1».")]
    public async Task A_form_sent_without_a_message_names_the_form_in_the_language_it_goes_out_in(
        string recipientCulture, bool translated, string expectedBody)
    {
        var fixture = await RecipientAsync(recipientCulture);
        var form = Form(translated, recipientCulture == "en" && translated ? "en" : "es", fixture);

        var sent = await SendAsync(fixture, form, null);

        Assert.True(sent.IsSuccess);
        Assert.Equal(expectedBody, sent.Value.Body);
        Assert.Equal(expectedBody, Assert.Single(fixture.Conversation.Outgoing).Body);
    }

    [Fact]
    public async Task A_message_written_by_the_operator_goes_out_as_written()
    {
        var fixture = await RecipientAsync("es");
        var form = Form(false, "es", fixture);

        var sent = await SendAsync(fixture, form, "  Pasá por la oficina antes de las 10.  ");

        Assert.True(sent.IsSuccess);
        Assert.Equal("Pasá por la oficina antes de las 10.", Assert.Single(fixture.Conversation.Outgoing).Body);
    }

    [Theory]
    [InlineData("es", "Te preparé el formulario «Visita» para que lo completes.")]
    [InlineData("en", "I prepared the form “Visit” for you to complete.")]
    public async Task An_assistant_offer_names_the_form_in_the_language_it_goes_out_in(string culture, string expectedBody)
    {
        var host = await WhatsAppAssistantSuggestionFixture.ReadyAsync(culture);
        var ready = host.Forms.Prefill.Value;
        host.Forms.Prefill = ready with
        {
            Form = ready.Form with
            {
                Name = new Dictionary<string, string> { ["es"] = "Visita", ["en"] = "Visit" },
                SubmitLabel = new Dictionary<string, string> { ["es"] = "Guardar", ["en"] = "Save" },
            },
            Culture = culture,
        };

        var sent = await host.Service.SendAsync(new(host.Context.Contact.Id, host.Request.RequestId, Guid.NewGuid(),
            [ready.Fields[0].FieldId]), Ct);

        Assert.True(sent.IsSuccess);
        Assert.Equal(expectedBody, Assert.Single(host.Conversation.Outgoing).Body);
    }

    private static async Task<WhatsAppAssistantSuggestionFixture> RecipientAsync(string culture)
    {
        var fixture = new WhatsAppAssistantSuggestionFixture();
        await fixture.Context.Users.UpdateProfileAsync(fixture.Context.Subject.Id, "Fictitious recipient", culture,
            "America/Argentina/Buenos_Aires", Ct);
        return fixture;
    }

    private static StructuredFormSelectionRow Form(bool translated, string sessionCulture, WhatsAppAssistantSuggestionFixture fixture)
    {
        var name = new Dictionary<string, string> { ["es"] = "Prueba renglones 1" };
        var submit = new Dictionary<string, string> { ["es"] = "Enviar" };
        if (translated) { name["en"] = "Rows test 1"; submit["en"] = "Send"; }
        var form = new StructuredFormSelectionRow(Guid.NewGuid(), Guid.NewGuid(), 4, "PRU", name, 5, submit);
        fixture.Forms.Prefill = new StructuredFormPrefillResponse(form, 1, [], [], "Selection reason", sessionCulture);
        fixture.Forms.Preparation = new StructuredFormPreparationResponse([], []) { Culture = sessionCulture };
        return form;
    }

    private static async Task<Result<WhatsAppOutgoingMessageResponse>> SendAsync(WhatsAppAssistantSuggestionFixture fixture,
        StructuredFormSelectionRow form, string? message)
    {
        var recipient = (await fixture.Context.Recipients.FindAsync(fixture.Context.Contact.Id, Ct))!;
        var request = new SendWhatsAppFormRequest(fixture.Context.Contact.Id, Guid.NewGuid(), form.DefinitionId, form.VersionId,
            fixture.Context.Contact.ModeRevision, WhatsAppFormRecipientRevision.Create(fixture.Context.Operator.Id, recipient), message);
        var service = new WhatsAppFormSendService(WhatsAppAssistantAcceptanceFixture.CreateSending(fixture),
            new WhatsAppFlowSessionIssuer(fixture.Protector, fixture.Context.Clock), fixture.Sessions, fixture.Conversation,
            RequestValidators.For(new SendWhatsAppFormRequestValidator()), fixture.Context.Current, fixture.UnitOfWork,
            NullLogger<WhatsAppFormSendService>.Instance);
        return await service.SendAsync(request, Ct);
    }
}
