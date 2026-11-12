using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

/// <summary>Derivar (AS4): the conversation goes to Atención manual and is marked «Necesita una persona» with its reason.</summary>
public sealed class WhatsAppContactHandoffTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Handing_off_takes_the_conversation_to_manual_and_keeps_the_reason()
    {
        var contact = AssistantContact();
        var revision = contact.ModeRevision;

        contact.HandOff("case", "  Pide hablar con una persona.  ", Now);

        Assert.Equal(WhatsAppConversationMode.Manual, contact.Mode);
        Assert.Equal(revision + 1, contact.ModeRevision);
        Assert.True(contact.NeedsPerson);
        Assert.Equal("case", contact.HandoffReasonCode);
        Assert.Equal("Pide hablar con una persona.", contact.HandoffReason);
        Assert.Equal(Now, contact.HandoffAtUtc);
    }

    [Fact]
    public void A_system_reason_has_a_code_and_no_text()
    {
        var contact = AssistantContact();

        contact.HandOff("dailyReplyLimit", null, Now);

        Assert.Equal("dailyReplyLimit", contact.HandoffReasonCode);
        Assert.Null(contact.HandoffReason);
    }

    [Fact]
    public void Handing_off_a_manual_conversation_marks_it_without_another_mode_revision()
    {
        var contact = WhatsAppContact.Create("5493413654813", null, "Ana", Now);
        var revision = contact.ModeRevision;

        contact.HandOff("case", "Motivo", Now);

        Assert.Equal(revision, contact.ModeRevision);
        Assert.True(contact.NeedsPerson);
    }

    [Fact]
    public void The_mark_goes_away_when_a_person_answers()
    {
        var contact = AssistantContact();
        contact.HandOff("case", "Motivo", Now);

        Assert.True(contact.ClearHandOff());
        Assert.False(contact.NeedsPerson);
        Assert.Null(contact.HandoffReasonCode);
        Assert.Null(contact.HandoffReason);
        Assert.Null(contact.HandoffAtUtc);
        Assert.False(contact.ClearHandOff());
    }

    [Fact]
    public void Going_back_to_the_assistant_clears_the_mark_and_manual_keeps_it()
    {
        var contact = AssistantContact();
        contact.HandOff("case", "Motivo", Now);

        contact.ChangeMode(WhatsAppConversationMode.Manual);
        Assert.True(contact.NeedsPerson);

        contact.ChangeMode(WhatsAppConversationMode.Assistant);
        Assert.False(contact.NeedsPerson);
    }

    [Theory]
    [InlineData("")]
    [InlineData("con espacio")]
    [InlineData("código")]
    public void A_reason_code_is_a_short_ascii_identifier(string code)
    {
        var contact = AssistantContact();

        Assert.Throws<ArgumentException>(() => contact.HandOff(code, null, Now));
        Assert.Equal(WhatsAppConversationMode.Assistant, contact.Mode);
    }

    [Fact]
    public void A_reason_has_its_limit_and_the_time_is_utc()
    {
        var contact = AssistantContact();

        Assert.Throws<ArgumentException>(() => contact.HandOff("case", new string('a', WhatsAppContact.MaxHandoffReasonLength + 1), Now));
        Assert.Throws<ArgumentException>(() => contact.HandOff("case", "Motivo", DateTime.SpecifyKind(Now, DateTimeKind.Local)));
        Assert.False(contact.NeedsPerson);
    }

    [Fact]
    public void The_fixed_message_goes_out_as_the_assistant_and_names_the_revision_of_the_handoff()
    {
        var contact = AssistantContact();
        contact.HandOff("case", "Motivo", Now);
        var recipient = Guid.CreateVersion7();
        var request = Guid.CreateVersion7();

        var outgoing = WhatsAppOutgoingMessage.CreateAssistantHandoff(recipient, contact.Id, request, "Te paso con una persona del equipo.",
            Now, contact.LinkRevision, "5493413654813", contact.ModeRevision);

        Assert.Equal(WhatsAppMessageOrigin.Assistant, outgoing.Origin);
        Assert.Equal(WhatsAppOutgoingMessageKind.Text, outgoing.Kind);
        Assert.True(outgoing.IsAssistantHandoff);
        Assert.Equal(contact.ModeRevision, outgoing.AssistantModeRevision);
        Assert.Equal(request, outgoing.RequestId);
        Assert.False(WhatsAppOutgoingMessage.CreateAssistantText(recipient, contact.Id, Guid.CreateVersion7(), "Hola", Now,
            contact.LinkRevision, "5493413654813", contact.ModeRevision).IsAssistantHandoff);
    }

    private static WhatsAppContact AssistantContact()
    {
        var contact = WhatsAppContact.Create("5493413654813", null, "Ana", Now);
        contact.ChangeMode(WhatsAppConversationMode.Assistant);
        return contact;
    }
}
