using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>Casos del asistente (H2 with a text): in Atención manual the suggestion can be a text for the operator's composer.</summary>
public sealed partial class WhatsAppAssistantSuggestionPolicyTests
{
    [Fact]
    public async Task A_case_text_is_a_suggestion_for_the_composer_of_the_operator()
    {
        var (host, reference) = await ReferenceAsync();
        var caseId = Guid.CreateVersion7();
        host.Assistant.Outcome = Completed(reference.RequestId, new(Guid.NewGuid(), Guid.NewGuid(), new("whatsapp.reply", 1),
            new WhatsAppAssistantReplyPayload("exactText", "own", caseId, "Pregunta por el horario", "Pregunta el horario.", "Abrimos de 9 a 18.", null, null, null)));

        var result = await new WhatsAppAssistantSuggestionPolicy(host.Reader, host.Assistant, new FakeAssistantForms(), host.Context.Clock, host.Presentation)
            .ReadAsync(reference, TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppAssistantRequestState.Ready, result.Value.Response.State);
        var text = result.Value.Response.TextProposal!;
        Assert.Equal(("Abrimos de 9 a 18.", WhatsAppAssistantCaseSource.Own, "Pregunta por el horario"), (text.Text, text.CaseSource, text.CaseTitle));
        Assert.Null(result.Value.Response.Proposal);
    }

    [Fact]
    public async Task A_checked_draft_is_suggested_with_what_it_used_and_a_rejected_one_is_not()
    {
        var (host, reference) = await ReferenceAsync();
        var policy = new WhatsAppAssistantSuggestionPolicy(host.Reader, host.Assistant, new FakeAssistantForms(), host.Context.Clock, host.Presentation);
        host.Assistant.Outcome = Completed(reference.RequestId, new(Guid.NewGuid(), Guid.NewGuid(), new("whatsapp.draft-reply", 1),
            new WhatsAppAssistantDraftPayload("fallback", null, null, "Abrimos de lunes a viernes de 9 a 18.", "Pregunta el horario.", ["Horario"], [])));

        var draft = await policy.ReadAsync(reference, TestContext.Current.CancellationToken);
        host.Assistant.Outcome = Completed(reference.RequestId, new(Guid.NewGuid(), Guid.NewGuid(), new("whatsapp.draft-reply", 1),
            new WhatsAppAssistantDraftPayload("fallback", null, null, "Reservá en https://otro.example", "Quiere reservar.", [], ["link"])));
        var rejected = await policy.ReadAsync(reference, TestContext.Current.CancellationToken);

        Assert.Equal((WhatsAppAssistantCaseSource.Fallback, "Horario"), (draft.Value.Response.TextProposal!.CaseSource, Assert.Single(draft.Value.Response.TextProposal.Used)));
        Assert.Equal(WhatsAppAssistantRequestState.NoReply, rejected.Value.Response.State);
        Assert.Null(rejected.Value.Response.TextProposal);
    }

    [Fact]
    public async Task A_handoff_leaves_nothing_to_suggest_in_atencion_manual()
    {
        var (host, reference) = await ReferenceAsync();
        host.Assistant.Outcome = Completed(reference.RequestId, new(Guid.NewGuid(), Guid.NewGuid(), new("whatsapp.reply", 1),
            new WhatsAppAssistantReplyPayload("handoff", "fallback", null, null, "Pide una persona.", "Te paso", null, "unknown", null)));

        var result = await new WhatsAppAssistantSuggestionPolicy(host.Reader, host.Assistant, new FakeAssistantForms(), host.Context.Clock, host.Presentation)
            .ReadAsync(reference, TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppAssistantRequestState.NoReply, result.Value.Response.State);
        Assert.Null(result.Value.Response.TextProposal);
    }
}
