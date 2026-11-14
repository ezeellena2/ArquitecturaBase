using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

/// <summary>
/// H19: each change of the mode of a conversation is added with who made it and when, also when the assistant hands it off. Not
/// with IAuditable: every message that arrives changes the contact.
/// </summary>
public sealed class WhatsAppContactModeChangeTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_contact_has_no_change()
    {
        Assert.Empty(Contact().ModeChanges);
    }

    [Fact]
    public void Changing_the_mode_adds_who_changed_it_and_when()
    {
        var contact = Contact();
        var actor = Guid.NewGuid();

        contact.ChangeMode(WhatsAppConversationMode.Assistant, actor, Now);

        var change = Assert.Single(contact.ModeChanges);
        Assert.Equal((contact.Id, 1L, WhatsAppConversationMode.Assistant, (Guid?)actor, Now),
            (change.ContactId, change.ModeRevision, change.Mode, change.ChangedByUserId, change.ChangedAtUtc));
    }

    [Fact]
    public void Handing_off_keeps_who_passed_the_chat_to_the_assistant()
    {
        var contact = Contact();
        var actor = Guid.NewGuid();
        contact.ChangeMode(WhatsAppConversationMode.Assistant, actor, Now);

        contact.HandOff("case", null, Now.AddMinutes(5));

        Assert.Equal([(WhatsAppConversationMode.Assistant, (Guid?)actor, Now), (WhatsAppConversationMode.Manual, (Guid?)null, Now.AddMinutes(5))],
            contact.ModeChanges.Select(change => (change.Mode, change.ChangedByUserId, change.ChangedAtUtc)));
    }

    [Fact]
    public void The_same_mode_adds_nothing()
    {
        var contact = Contact();
        var actor = Guid.NewGuid();
        contact.ChangeMode(WhatsAppConversationMode.Assistant, actor, Now);

        contact.ChangeMode(WhatsAppConversationMode.Assistant, Guid.NewGuid(), Now.AddHours(1));
        contact.ChangeMode(WhatsAppConversationMode.Manual, actor, Now.AddHours(2));
        contact.HandOff("dailyCap", null, Now.AddHours(3));

        Assert.Equal([1L, 2L], contact.ModeChanges.Select(change => change.ModeRevision));
    }

    [Fact]
    public void The_time_of_a_change_is_utc()
    {
        var contact = Contact();

        Assert.Throws<ArgumentException>(() => contact.ChangeMode(WhatsAppConversationMode.Assistant, Guid.NewGuid(),
            new DateTime(2026, 11, 14, 15, 0, 0, DateTimeKind.Unspecified)));
        Assert.Equal(WhatsAppConversationMode.Manual, contact.Mode);
        Assert.Empty(contact.ModeChanges);
    }

    private static WhatsAppContact Contact() => WhatsAppContact.Create("5493413654813", null, "Ana", Now);
}
