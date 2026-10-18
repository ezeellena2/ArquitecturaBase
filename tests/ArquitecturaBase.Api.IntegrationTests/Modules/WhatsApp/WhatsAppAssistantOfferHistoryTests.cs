using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantOfferHistoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Recent_offers_remain_readable_after_one_hundred_old_questions_and_declines_keep_their_own_scope()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        var actor = await host.ContactAsync(Ct, automatic: true);
        var other = await host.ContactAsync(Ct, automatic: true);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var oldRequests = new HashSet<Guid>();
        var latest = Guid.NewGuid();
        var declined = host.Forms.Form.DefinitionId;
        var privateDeclined = Guid.NewGuid();
        await host.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var contact = await db.Set<WhatsAppContact>().SingleAsync(row => row.Id == actor.ContactId, Ct);
            var otherContact = await db.Set<WhatsAppContact>().SingleAsync(row => row.Id == other.ContactId, Ct);
            for (var index = 0; index < 105; index++)
            {
                var requestId = Guid.NewGuid();
                oldRequests.Add(requestId);
                AddQuestion(db, host, actor, contact, requestId, now.AddDays(-index - 2));
            }
            AddQuestion(db, host, actor, contact, latest, now.AddMinutes(-1));
            AddDecline(db, actor, contact, Guid.NewGuid(), declined, now.AddDays(-2));
            AddDecline(db, other, otherContact, Guid.NewGuid(), privateDeclined, now.AddDays(-2));
            await db.SaveChangesAsync(Ct);
        });
        await host.ScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IWhatsAppAssistantReader>();
            var offers = await reader.ListOffersAsync(actor.RecipientId, Ct);
            Assert.Equal(2, offers.Count);
            Assert.All(offers, offer => Assert.Equal(latest, offer.RequestId));
            Assert.DoesNotContain(offers, offer => oldRequests.Contains(offer.RequestId));
            var declines = await reader.ListDeclinedDefinitionIdsAsync(actor.RecipientId, [declined, privateDeclined], Ct);
            Assert.Equal(declined, Assert.Single(declines));
            Assert.Empty(await reader.ListDeclinedDefinitionIdsAsync(actor.RecipientId, [privateDeclined], Ct));
            Assert.True(await reader.ExistsDeclinedAsync(actor.ContactId, declined, Ct));
            Assert.False(await reader.ExistsDeclinedAsync(other.ContactId, declined, Ct));
        });
    }

    private static void AddQuestion(ApplicationDbContext db, WhatsAppAssistantScenario host, WhatsAppAssistantContact actor,
        WhatsAppContact contact, Guid requestId, DateTime atUtc)
    {
        var source = Source(actor.ContactId, atUtc);
        var row = Reference(actor, contact, source.Id, requestId, atUtc);
        var first = host.Forms.Form; var second = host.Forms.AlternativeForm;
        var outgoing = WhatsAppOutgoingMessage.CreateAssistantQuickReplies(actor.RecipientId, actor.ContactId, requestId,
            "Fictitious historical question", [new(WhatsAppAssistantReplyCodec.Encode(requestId, first.DefinitionId, first.VersionId), "Visit", 1),
                new(WhatsAppAssistantReplyCodec.Encode(requestId, second.DefinitionId, second.VersionId), "Maintenance", 1)],
            atUtc, contact.LinkRevision, contact.WaId!, contact.ModeRevision);
        Assert.True(row.Ready(Guid.NewGuid(), null, atUtc));
        Assert.True(row.AttachQuestion(outgoing.Id, atUtc));
        db.AddRange(source, row, outgoing);
    }

    private static void AddDecline(ApplicationDbContext db, WhatsAppAssistantContact actor, WhatsAppContact contact,
        Guid requestId, Guid definitionId, DateTime atUtc)
    {
        var source = Source(actor.ContactId, atUtc);
        var row = Reference(actor, contact, source.Id, requestId, atUtc);
        Assert.True(row.RecordDecline(definitionId, atUtc));
        db.AddRange(source, row);
    }

    private static WhatsAppMessage Source(Guid contactId, DateTime atUtc) => WhatsAppMessage.Inbound(contactId,
        MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, "Fictitious historical request", null, atUtc);

    private static WhatsAppAssistantRequest Reference(WhatsAppAssistantContact actor, WhatsAppContact contact, Guid sourceId,
        Guid requestId, DateTime atUtc) => WhatsAppAssistantRequest.Create(requestId, actor.ContactId, sourceId, actor.RecipientId,
            actor.RecipientId, WhatsAppAssistantRequestKind.Automatic, contact.ModeRevision, contact.LinkRevision,
            JsonSerializer.Serialize(new { channelId = "fixture" }), atUtc, atUtc.AddDays(30));
}
