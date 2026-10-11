using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppContactLinkRevisionTests
{
    [Fact]
    public void Explicit_link_changes_detect_unlink_and_relink_to_the_same_account_without_counting_replays()
    {
        var contact = WhatsAppContact.Create("5491100000001", "synthetic-contact", "Synthetic", new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        Assert.Equal(0, contact.LinkRevision);
        contact.UnlinkUser(); Assert.Equal(0, contact.LinkRevision);
        contact.LinkUser(first); Assert.Equal(1, contact.LinkRevision);
        contact.LinkUser(first); Assert.Equal(1, contact.LinkRevision);
        contact.RecordInbound("5491100000001", "synthetic-contact", "Updated", contact.LastInboundAtUtc.AddMinutes(1));
        Assert.Equal(1, contact.LinkRevision);
        contact.UnlinkUser(); Assert.Equal(2, contact.LinkRevision);
        contact.LinkUser(first); Assert.Equal(3, contact.LinkRevision); Assert.Equal(first, contact.UserId);
        contact.LinkUser(second); Assert.Equal(4, contact.LinkRevision); Assert.Equal(second, contact.UserId);
    }
}
