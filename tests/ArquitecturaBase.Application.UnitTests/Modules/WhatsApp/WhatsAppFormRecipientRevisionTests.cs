using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFormRecipientRevisionTests
{
    [Fact]
    public void Repeated_reads_keep_the_revision_and_every_effective_recipient_or_actor_change_invalidates_it()
    {
        var actor = Guid.NewGuid();
        var row = new WhatsAppFormRecipientRow(Guid.NewGuid(), 3, "private-destination", null, Guid.NewGuid(),
            "Private name", "private@example.test", true, "es", "account-stamp", "roles-stamp");
        var revision = WhatsAppFormRecipientRevision.Create(actor, row);
        Assert.Equal(revision, WhatsAppFormRecipientRevision.Create(actor, row with { }));
        Assert.Equal(revision, WhatsAppFormRecipientRevision.Create(actor, row with { DisplayName = "Renamed", Email = null }));
        Assert.NotEqual(revision, WhatsAppFormRecipientRevision.Create(Guid.NewGuid(), row));
        foreach (var changed in new[]
        {
            row with { ContactId = Guid.NewGuid() }, row with { UserId = Guid.NewGuid() },
            row with { LinkRevision = 5 }, row with { WaId = "other-destination" },
            row with { UserIdentifier = "other-identifier" }, row with { Culture = "en" },
            row with { AccountStamp = "changed-account" }, row with { RoleFingerprint = "changed-roles" },
            row with { IsActive = false },
        }) Assert.NotEqual(revision, WhatsAppFormRecipientRevision.Create(actor, changed));
        Assert.DoesNotContain("private", revision, StringComparison.OrdinalIgnoreCase);
    }
}
