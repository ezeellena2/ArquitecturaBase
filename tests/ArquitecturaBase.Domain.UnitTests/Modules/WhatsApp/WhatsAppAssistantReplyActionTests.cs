using System.Text.Json;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

/// <summary>Casos del asistente: up to three actions per reply, each with its reference, and the why kept with what went out.</summary>
public sealed class WhatsAppAssistantReplyActionTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Another_action_of_a_reply_has_its_own_reference_with_the_operation_of_the_first()
    {
        var first = First();
        var second = WhatsAppAssistantRequest.CreateAction(first, Guid.CreateVersion7(), 1, Now);
        var third = WhatsAppAssistantRequest.CreateAction(first, Guid.CreateVersion7(), 2, Now);

        Assert.Equal(first.RequestId, second.OperationRequestId);
        Assert.Equal(first.RequestId, third.OperationRequestId);
        Assert.Equal((1, 2), (second.ActionOrdinal, third.ActionOrdinal));
        Assert.Equal(first.SourceMessageId, second.SourceMessageId);
        Assert.Equal(first.ContextJson, second.ContextJson);
        Assert.Equal(first.ContentExpiresAtUtc, second.ContentExpiresAtUtc);
        Assert.Equal(WhatsAppAssistantRequestState.Pending, second.State);
        Assert.Null(first.OperationRequestId);
        Assert.Equal(0, first.ActionOrdinal);
    }

    [Fact]
    public void A_reply_has_at_most_three_actions_and_they_belong_to_the_first_one()
    {
        var first = First();
        var second = WhatsAppAssistantRequest.CreateAction(first, Guid.CreateVersion7(), 1, Now);

        Assert.Throws<ArgumentException>(() => WhatsAppAssistantRequest.CreateAction(first, Guid.CreateVersion7(), 3, Now));
        Assert.Throws<ArgumentException>(() => WhatsAppAssistantRequest.CreateAction(first, Guid.CreateVersion7(), 0, Now));
        Assert.Throws<ArgumentException>(() => WhatsAppAssistantRequest.CreateAction(second, Guid.CreateVersion7(), 2, Now));
        Assert.Throws<ArgumentException>(() => WhatsAppAssistantRequest.CreateAction(first, Guid.CreateVersion7(), 1, Now.AddDays(2)));
    }

    [Fact]
    public void What_ended_without_a_message_keeps_its_case_once_until_the_content_expires()
    {
        var reference = First();

        Assert.True(reference.RecordOutcome("""{"kind":"NoReply","caseTitle":"Manda cadenas"}"""));
        Assert.True(reference.RecordOutcome("""{"kind":"NoReply","caseTitle":"Manda cadenas"}"""));
        Assert.False(reference.RecordOutcome("""{"kind":"NoReply","caseTitle":"Otro"}"""));
        Assert.False(First().RecordOutcome("not json"));
        reference.ClearOutcome();
        Assert.Null(reference.OutcomeJson);
    }

    [Fact]
    public void The_why_of_a_reply_goes_with_the_pending_message_once()
    {
        var outgoing = WhatsAppOutgoingMessage.CreateAssistantText(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Son las 10:42.",
            Now, 0, "5493413654813", 1);
        var why = """{"kind":"DraftedReply","ordinal":1,"count":1}""";

        outgoing.ExplainAssistantReply(why);
        outgoing.ExplainAssistantReply(why);

        Assert.Equal(why, outgoing.AssistantReplyJson);
        Assert.Throws<InvalidOperationException>(() => outgoing.ExplainAssistantReply("""{"kind":"ExactText"}"""));
        Assert.Throws<ArgumentException>(() => WhatsAppOutgoingMessage.CreateAssistantText(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "Hola", Now, 0, "5493413654813", 1).ExplainAssistantReply("[]"));
        var manual = WhatsAppOutgoingMessage.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Hola", Now);
        Assert.Throws<InvalidOperationException>(() => manual.ExplainAssistantReply(why));
    }

    [Fact]
    public void The_history_message_keeps_the_why_that_went_out()
    {
        var message = WhatsAppMessage.Outbound(Guid.CreateVersion7(), "wamid.1", WhatsAppMessageKind.Text, "Son las 10:42.", Now);

        message.AttributeAssistant(null, """{"kind":"DraftedReply"}""");

        Assert.Equal(WhatsAppMessageOrigin.Assistant, message.Origin);
        Assert.Equal("""{"kind":"DraftedReply"}""", message.AssistantReplyJson);
    }

    private static WhatsAppAssistantRequest First()
    {
        var actor = Guid.CreateVersion7();
        return WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), actor, actor,
            WhatsAppAssistantRequestKind.Automatic, 1, 0, JsonSerializer.Serialize(new { channelId = "whatsapp-assistant" }), Now, Now.AddDays(1));
    }
}
