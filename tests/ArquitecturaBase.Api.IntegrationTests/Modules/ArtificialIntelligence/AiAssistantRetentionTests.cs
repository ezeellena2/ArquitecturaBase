using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantRetentionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Purged_removed_content_keeps_the_receipt_until_its_deduplication_window_expires()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var conversation = AiConversation.Create(Guid.NewGuid(), nowUtc);
        var turn = AiAssistantTurn.Create(conversation.OwnerUserId, conversation.Id, Guid.NewGuid(), 1,
            "protected-question", "es", "UTC", AiProvider.OpenAI, "fictional-model", 1, nowUtc, nowUtc.AddDays(30));
        var receipt = AiAssistantRequestReceipt.Create(conversation.OwnerUserId, turn.RequestId, "fingerprint", "test",
            conversation.Id, turn.Id, nowUtc, nowUtc.AddDays(30), conversation.ChannelId, false);
        conversation.Remove(nowUtc);
        turn.RequestCancellation(nowUtc);
        await factory.InTransactionAsync(services =>
        {
            var db = services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>();
            db.AddRange(conversation, turn, receipt);
            return Task.CompletedTask;
        });
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().PurgeBatchAsync(Ct));
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Set<AiConversation>().AnyAsync(row => row.Id == conversation.Id, Ct));
            Assert.False(await db.Set<AiAssistantTurn>().AnyAsync(row => row.Id == turn.Id, Ct));
            var durable = await db.Set<AiAssistantTurn>().IgnoreQueryFilters().SingleAsync(row => row.Id == turn.Id, Ct);
            Assert.Null(durable.ProtectedQuestion);
            Assert.Null(durable.ProtectedAnswer);
            Assert.Equal(AiAssistantTurnState.Canceled, durable.State);
            Assert.True(await db.Set<AiAssistantRequestReceipt>().AnyAsync(row => row.Id == receipt.Id, Ct));
        });
    }

    [Fact]
    public async Task Removed_conversations_are_physically_purged_after_all_turns_expire_without_cascading_receipts()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var conversation = AiConversation.Create(Guid.NewGuid(), nowUtc.AddDays(-31));
        var turn = AiAssistantTurn.Create(conversation.OwnerUserId, conversation.Id, Guid.NewGuid(), 1,
            "protected-question", "es", "UTC", AiProvider.OpenAI, "fictional-model", 1, nowUtc.AddDays(-31), nowUtc.AddDays(-1));
        var receipt = AiAssistantRequestReceipt.Create(conversation.OwnerUserId, turn.RequestId, "fingerprint", "test",
            conversation.Id, turn.Id, nowUtc.AddDays(-29), nowUtc.AddDays(1), conversation.ChannelId, false);
        conversation.Remove(nowUtc.AddDays(-1));
        turn.RequestCancellation(nowUtc.AddDays(-1));
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().AddRange(conversation, turn, receipt);
            return Task.CompletedTask;
        });
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().PurgeBatchAsync(Ct));
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Set<AiConversation>().IgnoreQueryFilters().AnyAsync(row => row.Id == conversation.Id, Ct));
            Assert.False(await db.Set<AiAssistantTurn>().IgnoreQueryFilters().AnyAsync(row => row.Id == turn.Id, Ct));
            Assert.True(await db.Set<AiAssistantRequestReceipt>().AnyAsync(row => row.Id == receipt.Id, Ct));
        });
    }

    [Fact]
    public async Task An_expired_running_lease_with_a_reserved_step_becomes_uncertain_without_regeneration()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var conversation = AiConversation.Create(Guid.NewGuid(), nowUtc.AddMinutes(-3));
        var turn = AiAssistantTurn.Create(conversation.OwnerUserId, conversation.Id, Guid.NewGuid(), 1,
            "protected-question", "en", "UTC", AiProvider.OpenAI, "fictional-model", 1, nowUtc.AddMinutes(-3), nowUtc.AddDays(30));
        Assert.True(turn.Claim(Guid.NewGuid(), nowUtc.AddSeconds(-16)).IsSuccess);
        Assert.True(turn.BeginProviderStep(turn.Fence, nowUtc.AddSeconds(-106), 4).IsSuccess);
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().AddRange(conversation, turn);
            return Task.CompletedTask;
        });
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().ReconcileExpiredBatchAsync(Ct));
        await factory.ExecuteDbContextAsync(async db =>
        {
            var durable = await db.Set<AiAssistantTurn>().SingleAsync(row => row.Id == turn.Id, Ct);
            Assert.Equal(AiAssistantTurnState.Uncertain, durable.State);
            Assert.Equal(1, durable.ProviderSteps);
            Assert.True(durable.UsageUnknown);
        });
        var claim = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct));
        Assert.Null(claim.Value);
    }
    [Theory]
    [InlineData("whatsapp-assistant", false, false, false, true)]
    [InlineData("whatsapp-manual", false, false, false, true)]
    [InlineData("whatsapp-assistant", true, false, false, false)]
    [InlineData("whatsapp-assistant", false, true, false, false)]
    [InlineData("whatsapp-assistant", false, false, true, false)]
    [InlineData("home", false, false, false, false)]
    public async Task Channel_metadata_waits_for_turns_and_receipts_while_home_keeps_explicit_removal(
        string channel, bool liveReceipt, bool liveContent, bool activeTurn, bool removed)
    {
        var owner = await factory.InTransactionAsync(async services =>
            (await services.GetRequiredService<IUserRepository>().CreateAsync(Email.Create(TestEmails.Unique("channel-retention")).Value,
                null, false, null, "es", Ct)).Id);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var submitted = now.AddDays(-32);
        var conversation = AiConversation.Create(owner, submitted, channel);
        var turn = AiAssistantTurn.Create(owner, conversation.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC",
            AiProvider.OpenAI, "fictional-model", 1, submitted, now.AddDays(liveContent ? 1 : -1), channel);
        if (!activeTurn) turn.RequestCancellation(now.AddDays(-2));
        var receipt = AiAssistantRequestReceipt.Create(owner, turn.RequestId, "fingerprint", "test", conversation.Id, turn.Id,
            submitted, now.AddDays(liveReceipt ? 1 : -1), channel, false);
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().AddRange(conversation, turn, receipt);
            return Task.CompletedTask;
        });
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().PurgeBatchAsync(Ct));
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(!removed, await db.Set<AiConversation>().IgnoreQueryFilters().AnyAsync(row => row.Id == conversation.Id, Ct));
            Assert.Equal(!removed, await db.Set<AiAssistantTurn>().IgnoreQueryFilters().AnyAsync(row => row.Id == turn.Id, Ct));
            if (liveReceipt) Assert.True(await db.Set<AiAssistantRequestReceipt>().AnyAsync(row => row.Id == receipt.Id, Ct));
        });
    }
}
