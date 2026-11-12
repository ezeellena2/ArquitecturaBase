using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>Casos del asistente: «Por qué respondió esto» in the thread, and the lines of what ended without a message.</summary>
public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    [Fact]
    public async Task A_reply_of_the_agent_shows_why_it_answered_and_what_ended_without_a_message_is_a_line()
    {
        var host = new Host();
        var caseId = Guid.CreateVersion7();
        host.Reader.ReplyJson = WhatsAppAssistantReplyRecordCodec.Serialize(new(WhatsAppAssistantActionKind.ExactText, WhatsAppAssistantCaseSource.Own, caseId,
            "Pregunta por el horario", "Pregunta el horario.", [], 1, 2));
        var noReply = Guid.CreateVersion7();
        host.Reader.Events =
        [
            new(noReply, Now, WhatsAppAssistantReplyRecordCodec.Serialize(new(WhatsAppAssistantActionKind.NoReply, WhatsAppAssistantCaseSource.Fallback, null, null,
                "Es un saludo de cierre.", [], 1, 1))),
            new(Guid.CreateVersion7(), Now, "{\"kind\":\"NoReply\""),
        ];

        var thread = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);

        var reply = Assert.Single(thread.Value.Messages).AssistantReply!;
        Assert.Equal((WhatsAppAssistantActionKind.ExactText, WhatsAppAssistantCaseSource.Own, "Pregunta por el horario", "Pregunta el horario.", 1, 2),
            (reply.Kind, reply.CaseSource, reply.CaseTitle, reply.Reason, reply.Ordinal, reply.Count));
        Assert.Equal(WhatsAppAssistantActionKind.ExactText, Assert.Single(thread.Value.Outgoing).AssistantReply!.Kind);
        // A line whose why does not read is left out, never guessed.
        var line = Assert.Single(thread.Value.AssistantEvents!);
        Assert.Equal((noReply, WhatsAppAssistantActionKind.NoReply, "Es un saludo de cierre."), (line.Id, line.Reply.Kind, line.Reply.Reason));
    }

    [Fact]
    public async Task Messages_without_a_why_keep_their_shape()
    {
        var host = new Host();

        var thread = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);

        Assert.Null(Assert.Single(thread.Value.Messages).AssistantReply);
        Assert.Null(Assert.Single(thread.Value.Outgoing).AssistantReply);
        Assert.Null(thread.Value.AssistantEvents);
    }
}
