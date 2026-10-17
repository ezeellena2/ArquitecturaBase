using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppFormActionReceiptTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Observation_sees_early_receipts_without_history_and_keeps_requested_pairs_separate()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var now = MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);
        var first = await scenario.RecipientAsync(now, Ct);
        var second = await scenario.RecipientAsync(now, Ct);
        var firstRun = Guid.NewGuid();
        var secondRun = Guid.NewGuid();
        var firstWamid = "wamid.receipt-" + Guid.NewGuid().ToString("N");
        var secondWamid = "wamid.receipt-" + Guid.NewGuid().ToString("N");
        await scenario.InTransactionAsync(services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.AddRange(Accepted(firstRun, first.UserId, first.ContactId, firstWamid, now),
                Accepted(secondRun, second.UserId, second.ContactId, secondWamid, now),
                Accepted(secondRun, first.UserId, first.ContactId, "wamid.unrequested-" + Guid.NewGuid().ToString("N"), now));
            return Task.FromResult(true);
        }, Ct);
        await scenario.ScopeAsync(services => services.GetRequiredService<IWhatsAppWebhookPersistence>().PersistAsync(
            new WhatsAppWebhookBatch([], [
                new(firstWamid, WhatsAppMessageStatus.Read, now.AddMinutes(2), null),
                new(firstWamid, WhatsAppMessageStatus.Delivered, now.AddMinutes(1), null),
                new(secondWamid, WhatsAppMessageStatus.Read, now.AddMinutes(3), null)]), Ct));
        var requests = new StructuredRecordActionObservationRequest[]
        {
            new(firstRun, first.UserId), new(secondRun, second.UserId), new(firstRun, second.UserId)
        };
        var observed = await scenario.ScopeAsync(services => services.GetRequiredService<IWhatsAppFormActionService>()
            .ObserveAsync(requests, Ct));
        Assert.Equal(2, observed.Count);
        var firstStatus = Assert.Single(observed, item => item.RunId == firstRun).Status;
        Assert.Equal(StructuredRecordActionState.Accepted, firstStatus.State);
        Assert.Equal(now.AddMinutes(1), firstStatus.Receipt!.DeliveredAtUtc);
        Assert.Equal(now.AddMinutes(2), firstStatus.Receipt.ReadAtUtc);
        var secondStatus = Assert.Single(observed, item => item.RunId == secondRun).Status;
        Assert.Null(secondStatus.Receipt!.DeliveredAtUtc);
        Assert.Equal(now.AddMinutes(3), secondStatus.Receipt.ReadAtUtc);
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Set<WhatsAppMessage>().AnyAsync(item => item.WaMessageId == firstWamid || item.WaMessageId == secondWamid, Ct));
            Assert.All(await db.Set<WhatsAppOutgoingMessage>().AsNoTracking().Where(item => item.RequestId == firstRun || item.RequestId == secondRun).ToArrayAsync(Ct),
                item => Assert.Equal(WhatsAppOutgoingMessageState.Accepted, item.State));
        });
        Assert.Empty(scenario.Cloud.Messages);
        Assert.Equal(0, scenario.Media.Calls);
    }

    [Fact]
    public async Task A_legacy_delivered_row_preserves_its_real_date_when_read_arrives_later()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var now = MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);
        var recipient = await scenario.RecipientAsync(now, Ct);
        var run = Guid.NewGuid();
        var wamid = "wamid.legacy-" + Guid.NewGuid().ToString("N");
        await scenario.InTransactionAsync(services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Add(Accepted(run, recipient.UserId, recipient.ContactId, wamid, now));
            return Task.FromResult(true);
        }, Ct);
        // Seed the real pre-migration shape: only the last observed status has a known timestamp.
        await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WhatsAppDeliveryReceipts" ("Id", "WaMessageId", "Status", "OccurredAtUtc", "ErrorCode")
            VALUES ({Guid.NewGuid()}, {wamid}, 'Delivered', {now}, NULL)
            """, Ct));
        var request = new StructuredRecordActionObservationRequest[] { new(run, recipient.UserId) };
        var before = await scenario.ScopeAsync(services => services.GetRequiredService<IWhatsAppFormActionService>().ObserveAsync(request, Ct));
        Assert.Equal(now, Assert.Single(before).Status.Receipt!.DeliveredAtUtc);
        Assert.Null(Assert.Single(before).Status.Receipt!.ReadAtUtc);
        await scenario.ScopeAsync(services => services.GetRequiredService<IWhatsAppWebhookPersistence>().PersistAsync(
            new WhatsAppWebhookBatch([], [new(wamid, WhatsAppMessageStatus.Read, now.AddMinutes(1), null)]), Ct));
        var after = await scenario.ScopeAsync(services => services.GetRequiredService<IWhatsAppFormActionService>().ObserveAsync(request, Ct));
        Assert.Equal(now, Assert.Single(after).Status.Receipt!.DeliveredAtUtc);
        Assert.Equal(now.AddMinutes(1), Assert.Single(after).Status.Receipt!.ReadAtUtc);
        await scenario.ScopeAsync(async services =>
        {
            var receipt = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppDeliveryReceipt>().AsNoTracking()
                .SingleAsync(item => item.WaMessageId == wamid, Ct);
            Assert.Equal(WhatsAppMessageStatus.Read, receipt.Status);
            Assert.Equal(now, receipt.DeliveredAtUtc);
            Assert.Equal(now.AddMinutes(1), receipt.ReadAtUtc);
        });
        Assert.Empty(scenario.Cloud.Messages);
    }

    private static WhatsAppOutgoingMessage Accepted(Guid runId, Guid userId, Guid contactId, string wamid, DateTime nowUtc)
    {
        var outgoing = WhatsAppOutgoingMessage.CreateFormAction(userId, contactId, runId, "Private captured answer", nowUtc);
        var reservation = outgoing.ReserveFormAction(nowUtc);
        Assert.True(outgoing.MarkFormActionMessageEffect(reservation, nowUtc));
        outgoing.Accept(wamid);
        return outgoing;
    }
}
