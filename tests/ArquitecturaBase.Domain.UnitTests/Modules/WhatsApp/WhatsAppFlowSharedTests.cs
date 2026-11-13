using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

// W3 (task 23): an open send takes one location and one contact that the person shares on their own and keeps what it took without
// values; what was written goes protected and leaves with the retention of the messages. A resend keeps its mark on the outgoing and
// on the message it leaves.
public sealed class WhatsAppFlowSharedTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_open_send_takes_one_of_each_kind_and_keeps_when_and_which_questions_without_values()
    {
        var session = Session(Now.AddMinutes(-10));
        var place = Guid.CreateVersion7();
        var name = Guid.CreateVersion7();
        var phone = Guid.CreateVersion7();

        Assert.True(session.TakeShared(WhatsAppMessageKind.Location, [place], Now.AddMinutes(-1), Now));
        Assert.False(session.TakeShared(WhatsAppMessageKind.Location, [place], Now.AddMinutes(-1), Now));
        Assert.True(session.TakeShared(WhatsAppMessageKind.Contact, [name, phone], Now.AddMinutes(-1), Now.AddSeconds(1)));

        Assert.Equal(Now, session.SharedLocation!.AtUtc);
        Assert.Equal([place], session.SharedLocation.FieldIds);
        Assert.Equal(Now.AddSeconds(1), session.SharedContact!.AtUtc);
        Assert.Equal([name, phone], session.SharedContact.FieldIds);
        Assert.DoesNotContain("Av.", session.SharedContentJson, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.TakeShared(WhatsAppMessageKind.Text, [place], Now, Now));
        Assert.Throws<ArgumentException>(() => session.TakeShared(WhatsAppMessageKind.Contact, [], Now, Now));
    }

    // Task 24: the chat and the activity read what a send took from its stored JSON, without the entity.
    [Fact]
    public void What_a_send_took_reads_from_its_stored_json_and_is_empty_when_it_took_nothing()
    {
        var session = Session(Now.AddMinutes(-10));
        var place = Guid.CreateVersion7();
        Assert.True(session.TakeShared(WhatsAppMessageKind.Location, [place], Now.AddMinutes(-1), Now));

        var shared = WhatsAppFlowSession.ReadSharedContent(session.SharedContentJson);

        Assert.Equal(Now, shared.Location!.AtUtc);
        Assert.Equal([place], shared.Location.FieldIds);
        Assert.Null(shared.Contact);
        var none = WhatsAppFlowSession.ReadSharedContent(null);
        Assert.Null(none.Location);
        Assert.Null(none.Contact);
    }

    [Fact]
    public void A_send_is_open_only_if_it_existed_went_out_has_no_reply_and_has_not_expired()
    {
        var session = Session(Now.AddMinutes(-10));
        Assert.False(session.IsOpenFor(Now.AddMinutes(-11), Now));
        Assert.True(session.IsOpenFor(Now.AddMinutes(-1), Now));
        Assert.False(session.IsOpenFor(Now.AddMinutes(-1), session.ExpiresAtUtc));

        var waiting = Session(Now.AddMinutes(-10));
        waiting.RequestLocation(true, "Completá el formulario.");
        Assert.False(waiting.IsOpenFor(Now.AddMinutes(-1), Now));
        Assert.True(waiting.ReceiveLocation(-34.6m, -58.38m, "wamid.location", Now));
        Assert.False(waiting.IsOpenFor(Now.AddMinutes(-1), Now.AddMinutes(1)));
        Assert.True(waiting.IsOpenFor(Now.AddMinutes(1), Now.AddMinutes(1)));

        var answered = Session(Now.AddMinutes(-10));
        answered.CompleteCapture("wamid.reply", WhatsAppFlowCaptureState.Completed, Guid.CreateVersion7(), "ENT-000001", null);
        Assert.False(answered.TakeShared(WhatsAppMessageKind.Contact, [Guid.CreateVersion7()], Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void What_was_written_is_sealed_after_a_take_replaced_by_the_next_and_cleared_by_the_retention()
    {
        var session = Session(Now.AddMinutes(-10));
        Assert.Throws<InvalidOperationException>(() => session.SealSharedValues([1]));
        session.TakeShared(WhatsAppMessageKind.Location, [Guid.CreateVersion7()], Now.AddMinutes(-1), Now);

        session.SealSharedValues([1, 2]);
        session.SealSharedValues([3]);

        Assert.Equal(new byte[] { 3 }, session.ProtectedSharedValues);
        Assert.True(session.ClearSharedValues());
        Assert.Null(session.ProtectedSharedValues);
        Assert.False(session.ClearSharedValues());
        Assert.NotNull(session.SharedLocation);
    }

    [Fact]
    public void A_resend_is_marked_once_on_its_queued_flow_and_on_the_message_it_leaves()
    {
        var session = Session(Now.AddMinutes(-10));
        var flow = WhatsAppOutgoingMessage.CreateFlow(Guid.CreateVersion7(), session.ContactId, Guid.CreateVersion7(), "Puse tu ubicación.", session.Id, Now);
        var text = WhatsAppOutgoingMessage.Create(Guid.CreateVersion7(), session.ContactId, Guid.CreateVersion7(), "Hola", Now);

        flow.MarkResend();

        Assert.True(flow.IsResend);
        Assert.Throws<InvalidOperationException>(flow.MarkResend);
        Assert.Throws<InvalidOperationException>(text.MarkResend);
        var sent = WhatsAppMessage.Outbound(session.ContactId, "wamid.resend", WhatsAppMessageKind.Flow, "Puse tu ubicación.", Now);
        Assert.Throws<InvalidOperationException>(sent.MarkResend);
        sent.AttachFlowSession(session.Id);
        sent.MarkResend();
        Assert.True(sent.IsResend);
        var contact = WhatsAppMessage.Inbound(session.ContactId, "wamid.contact", WhatsAppMessageKind.Contact, null, null, Now);
        contact.AttachFlowSession(session.Id);
        Assert.Equal(session.Id, contact.FlowSessionId);
    }

    private static WhatsAppFlowSession Session(DateTime createdAt)
    {
        var session = WhatsAppFlowSession.Create(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, Guid.CreateVersion7(), "es", "{\"es\":\"Registrar entrega\"}", "ENT", new('a', 64), new('b', 64), Now.AddHours(20));
        typeof(WhatsAppFlowSession).GetProperty(nameof(WhatsAppFlowSession.CreatedAtUtc))!.SetValue(session, createdAt);
        return session;
    }
}
