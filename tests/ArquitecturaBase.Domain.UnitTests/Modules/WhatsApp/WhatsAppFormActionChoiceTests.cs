using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFormActionChoiceTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly WhatsAppFormActionChoice[] Choices = [new("issue", "Emitir"), new("skip", "No emitir")];

    private static WhatsAppOutgoingMessage Create(IReadOnlyList<WhatsAppFormActionChoice>? choices = null, WhatsAppOutgoingDocument? document = null, string body = "¿La emito?") =>
        WhatsAppOutgoingMessage.CreateFormAction(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), body, Now, document, choices: choices);

    [Fact]
    public void Choices_fix_reply_buttons_that_name_the_outgoing_and_the_choice()
    {
        var outgoing = Create(Choices);

        Assert.Equal(WhatsAppOutgoingMessageKind.QuickReplies, outgoing.Kind);
        Assert.Equal(["Emitir", "No emitir"], outgoing.QuickReplies.Select(reply => reply.Title));
        Assert.Equal([WhatsAppFormActionChoice.ReplyId(outgoing.Id, "issue"), WhatsAppFormActionChoice.ReplyId(outgoing.Id, "skip")],
            outgoing.QuickReplies.Select(reply => reply.Id));
        Assert.All(outgoing.QuickReplies, reply => Assert.True(reply.Id.Length <= 256));
        Assert.True(outgoing.HasFormActionChoices(Choices));
        Assert.False(outgoing.HasFormActionChoices([new("issue", "Emitir")]));
        Assert.False(outgoing.HasFormActionChoices(null));
    }

    [Fact]
    public void An_action_without_choices_stays_plain_text_and_matches_only_no_choices()
    {
        var outgoing = Create([]);

        Assert.Equal(WhatsAppOutgoingMessageKind.Text, outgoing.Kind);
        Assert.Empty(outgoing.QuickReplies);
        Assert.True(outgoing.HasFormActionChoices(null));
        Assert.True(outgoing.HasFormActionChoices([]));
        Assert.False(outgoing.HasFormActionChoices(Choices));
    }

    [Fact]
    public void A_retry_resends_the_same_buttons()
    {
        var outgoing = Create(Choices);
        var sent = outgoing.QuickReplies;
        outgoing.Fail("SendRejected");

        Assert.True(outgoing.RetryFormAction(2, Now.AddMinutes(1)));
        Assert.Equal(sent, outgoing.QuickReplies);
        Assert.Equal(WhatsAppOutgoingMessageKind.QuickReplies, outgoing.Kind);
    }

    [Fact]
    public void Choices_reject_a_document_more_than_three_buttons_repeated_ids_or_titles_and_a_long_body()
    {
        var document = new WhatsAppOutgoingDocument("private/action.pdf", "Factura.pdf", "application/pdf", 120, new string('a', 64));

        Assert.Throws<ArgumentException>(() => Create(Choices, document));
        Assert.Throws<ArgumentException>(() => Create([new("a", "A"), new("b", "B"), new("c", "C"), new("d", "D")]));
        Assert.Throws<ArgumentException>(() => Create([new("same", "Uno"), new("same", "Dos")]));
        Assert.Throws<ArgumentException>(() => Create([new("one", "Igual"), new("two", "Igual")]));
        Assert.Throws<ArgumentException>(() => Create(Choices, body: new string('x', 1025)));
        Assert.Equal(1024, Create(Choices, body: new string('x', 1024)).Body!.Length);
    }

    [Theory]
    [InlineData("", "Emitir")]
    [InlineData(" issue", "Emitir")]
    [InlineData("issue\n", "Emitir")]
    [InlineData("123456789012345678901234567890123", "Emitir")]
    [InlineData("issue", "")]
    [InlineData("issue", "123456789012345678901")]
    [InlineData("issue", "​")]
    public void A_choice_outside_the_provider_limits_is_invalid(string id, string title)
    {
        Assert.False(WhatsAppFormActionChoice.AreValid([new(id, title)]));
        Assert.Throws<ArgumentException>(() => Create([new(id, title)]));
    }

    [Fact]
    public void A_reply_id_parses_back_only_with_its_prefix_a_compact_id_and_a_valid_choice()
    {
        var outgoingId = Guid.CreateVersion7();
        var replyId = WhatsAppFormActionChoice.ReplyId(outgoingId, "issue:v2");

        Assert.True(WhatsAppFormActionChoice.TryParseReplyId(replyId, out var parsed, out var choice));
        Assert.Equal(outgoingId, parsed);
        Assert.Equal("issue:v2", choice);
        Assert.False(WhatsAppFormActionChoice.TryParseReplyId(null, out _, out _));
        Assert.False(WhatsAppFormActionChoice.TryParseReplyId("FORM_ASSISTANT:" + outgoingId.ToString("N") + ":issue", out _, out _));
        Assert.False(WhatsAppFormActionChoice.TryParseReplyId(WhatsAppFormActionChoice.ReplyPrefix + outgoingId.ToString("N") + ":", out _, out _));
        Assert.False(WhatsAppFormActionChoice.TryParseReplyId(WhatsAppFormActionChoice.ReplyPrefix + outgoingId.ToString("D") + ":issue", out _, out _));
        Assert.False(WhatsAppFormActionChoice.TryParseReplyId(WhatsAppFormActionChoice.ReplyPrefix + Guid.Empty.ToString("N") + ":issue", out _, out _));
        Assert.False(WhatsAppFormActionChoice.TryParseReplyId(WhatsAppFormActionChoice.ReplyPrefix + outgoingId.ToString("N") + ":" + new string('x', 33), out _, out _));
        Assert.DoesNotContain("Emitir", new WhatsAppFormActionChoice("issue", "Emitir").ToString(), StringComparison.Ordinal);
    }
}
