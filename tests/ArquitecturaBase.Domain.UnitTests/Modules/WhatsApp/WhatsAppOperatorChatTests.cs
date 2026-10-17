using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppOperatorChatTests
{
    [Theory]
    [InlineData("\0", false)]
    [InlineData("\u200B", false)]
    [InlineData(" \n\t", false)]
    [InlineData("line one\nline two\tvisible", true)]
    [InlineData("👨‍👩‍👧‍👦", true)]
    [InlineData("\u0301", false)]
    public void Flow_message_body_rejects_unstorable_or_invisible_text_but_preserves_layout(string text, bool expected) =>
        Assert.Equal(expected, WhatsAppOutgoingMessage.IsVisibleBody(text));

    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Switching_mode_keeps_contact_identity_and_advances_revision_only_on_change()
    {
        var contact = WhatsAppContact.Create("5493413654813", null, "Ana", Now);
        Assert.Equal(WhatsAppConversationMode.Manual, contact.Mode);
        contact.ChangeMode(WhatsAppConversationMode.Assistant);
        Assert.Equal(1, contact.ModeRevision);
        contact.ChangeMode(WhatsAppConversationMode.Assistant);
        Assert.Equal(1, contact.ModeRevision);
        Assert.Equal("5493413654813", contact.WaId);
    }

    [Fact]
    public void Read_position_never_moves_backwards_and_belongs_to_one_operator()
    {
        var state = WhatsAppReadState.Create(Guid.NewGuid(), Guid.NewGuid());
        state.Advance(5);
        state.Advance(2);
        Assert.Equal(5, state.LastReadSequence);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Advance(-1));
    }

    [Fact]
    public void A_reserved_send_is_not_repeated_and_expiration_is_uncertain()
    {
        var operation = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        Assert.Equal(WhatsAppOutgoingMessageState.Pending, operation.State);
        operation.Start(Now);
        Assert.Throws<InvalidOperationException>(() => operation.Start(Now));
        operation.MarkUncertain();
        Assert.Equal(WhatsAppOutgoingMessageState.Uncertain, operation.State);
        Assert.Throws<InvalidOperationException>(() => operation.Start(Now));
        operation.Accept("wamid.confirmed");
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted, operation.State);
        Assert.Equal("wamid.confirmed", operation.WaMessageId);
    }

    [Fact]
    public void Accepted_sends_cannot_be_changed_to_failure()
    {
        var operation = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        operation.Start(Now);
        operation.Accept("wamid.confirmed");
        Assert.Throws<InvalidOperationException>(() => operation.Fail());
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted, operation.State);
    }
}
