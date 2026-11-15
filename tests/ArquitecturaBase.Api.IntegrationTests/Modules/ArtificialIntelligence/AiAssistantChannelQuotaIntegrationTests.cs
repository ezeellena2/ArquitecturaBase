using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantChannelQuotaIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Real_acceptance_keeps_home_text_and_audio_quotas_separate_after_content_is_deleted()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "fixture.proposal", services =>
        {
            services.AddSingleton<IAssistantCapabilitySource>(new RoundtripAssistantSource());
            services.PostConfigure<AiAssistantOptions>(options =>
            {
                options.Limits = options.Limits with { ActorDailyQuota = 1 };
                options.ModelProfiles = options.ModelProfiles.Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                    .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray();
                options.AudioModelProfiles = [new(AiProvider.OpenAI, "fictional-audio", "fixture-audio", 1, ["audio/ogg"], 25000000)];
            });
        });
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var homeId = Guid.NewGuid();
        await scenario.SubmitAsync(homeId, "Fictitious Home question");
        using (var canceled = await scenario.Client.PostAsync($"/api/ai/assistant/turns/{homeId}/cancel", null, Ct)) Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        var context = new AssistantChannelContext("fixture-quota", actor, actor, "en", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        var text = new AssistantOperationRequest(Guid.NewGuid(), context, "Fictitious visit", now, now.AddHours(1));
        var audio = text with { RequestId = Guid.NewGuid(), Text = "", Context = context with { AllowedCapabilities = [], AllowedTerminals = [] },
            Audio = new("fictional/private-audio", "audio.ogg", "audio/ogg", 42, new string('a', 64), now.AddHours(1)) };
        foreach (var request in new[] { text, audio })
        {
            var accepted = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct));
            Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
            Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().CancelAsync(request.Context, request.RequestId, Ct))).IsSuccess);
        }
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var rows = await db.Set<AiAssistantRequestReceipt>().Where(receipt => receipt.OwnerUserId == actor).ToArrayAsync(Ct);
            Assert.Contains(rows, receipt => receipt.RequestId == homeId && receipt.ChannelId == "home" && receipt.IsAudio == false);
            Assert.Contains(rows, receipt => receipt.RequestId == text.RequestId && receipt.ChannelId == context.ChannelId && receipt.IsAudio == false);
            Assert.Contains(rows, receipt => receipt.RequestId == audio.RequestId && receipt.ChannelId == context.ChannelId && receipt.IsAudio == true);
            db.Set<AiConversation>().RemoveRange(await db.Set<AiConversation>().Where(conversation => conversation.OwnerUserId == actor && conversation.ChannelId == context.ChannelId).ToArrayAsync(Ct));
            await db.SaveChangesAsync(Ct);
            return true;
        });
        foreach (var request in new[] { text, audio })
        {
            var blocked = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request with { RequestId = Guid.NewGuid() }, Ct));
            Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, blocked.Error.Code);
        }
        var other = text with { RequestId = Guid.NewGuid(), Context = context with { ChannelId = "fixture-another-quota" } };
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(other, Ct))).IsSuccess);
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().CancelAsync(other.Context, other.RequestId, Ct))).IsSuccess);
        Assert.Empty(scenario.Handler.Requests);
    }

    [Fact]
    public async Task Unknown_historical_dimensions_count_conservatively_without_losing_known_channel_attribution()
    {
        var actor = Guid.CreateVersion7(); var now = factory.Clock.GetUtcNow().UtcDateTime;
        var unknown = AiAssistantRequestReceipt.Create(actor, Guid.NewGuid(), "fictional", "test", Guid.NewGuid(), Guid.NewGuid(), now, now.AddDays(1), "unknown-before-update", false);
        var knownChannel = AiAssistantRequestReceipt.Create(actor, Guid.NewGuid(), "fictional", "test", Guid.NewGuid(), Guid.NewGuid(), now, now.AddDays(1), "known-channel", false);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<AiAssistantRequestReceipt>().AddRange(unknown, knownChannel);
            await db.SaveChangesAsync(Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "AiAssistantRequestReceipts" SET "ChannelId" = NULL, "IsAudio" = NULL WHERE "Id" = {unknown.Id}""", Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "AiAssistantRequestReceipts" SET "IsAudio" = NULL WHERE "Id" = {knownChannel.Id}""", Ct);
        });
        try
        {
        await factory.ExecuteScopeAsync(async services =>
        {
            var repository = services.GetRequiredService<IAiAssistantRepository>();
            foreach (var audio in new[] { false, true })
            {
                Assert.Equal(1, (await repository.CountBudgetAsync(actor, DateOnly.FromDateTime(now), "other-channel", audio, false, Ct)).ActorAccepted);
                Assert.Equal(2, (await repository.CountBudgetAsync(actor, DateOnly.FromDateTime(now), "known-channel", audio, false, Ct)).ActorAccepted);
            }
        });
        }
        finally
        {
            await factory.ExecuteDbContextAsync(async db =>
            {
                db.Set<AiAssistantRequestReceipt>().RemoveRange(await db.Set<AiAssistantRequestReceipt>().Where(receipt => receipt.OwnerUserId == actor).ToArrayAsync(Ct));
                await db.SaveChangesAsync(Ct);
            });
        }
    }

    // I1: four lanes and turns of 80 seconds start eight of twelve within the 120 pending seconds. The four that expire without an
    // attempt give their quota back and still deduplicate their id; a turn that reserved a step keeps counting, even when it ends uncertain.
    // Twelve may wait only because the test raises the cap: the default, 4, is what four lanes start before any of them expires.
    // The lanes run one after the other on the shared clock (the fictitious provider pairs its answers by order).
    [Fact]
    public async Task A_batch_of_long_turns_gives_back_the_quota_of_those_that_expired_without_an_attempt()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary", services =>
            services.PostConfigure<AiAssistantOptions>(options => options.Limits = options.Limits with { ActorDailyQuota = 12, PendingCapacity = 12 }));
        // The quota counts by UTC day: the batch must not cross midnight on the shared clock, which only moves forward.
        var untilMidnight = factory.Clock.GetUtcNow().UtcDateTime.Date.AddDays(1) - factory.Clock.GetUtcNow().UtcDateTime;
        if (untilMidnight < TimeSpan.FromMinutes(10)) factory.Clock.Advance(untilMidnight + TimeSpan.FromMinutes(1));
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var batch = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var requestId in batch) await scenario.SubmitAsync(requestId, "Fictitious long question");
        for (var lane = 0; lane < 4; lane++) Assert.True(await scenario.ProcessNextAsync());
        factory.Clock.Advance(TimeSpan.FromSeconds(80));
        for (var lane = 0; lane < 3; lane++) Assert.True(await scenario.ProcessNextAsync());
        // The fourth lane reserves its step and its copy stops answering: the turn ends uncertain.
        await scenario.ScopeAsync(async services =>
        {
            var dispatch = services.GetRequiredService<IAiAssistantDispatchService>();
            var claim = (await dispatch.ClaimAsync(Ct)).Value!;
            Assert.True((await dispatch.BeginProviderStepAsync(claim, Ct)).Value);
            return claim;
        });
        factory.Clock.Advance(TimeSpan.FromSeconds(41));
        Assert.Equal(4, (await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().ReconcileExpiredBatchAsync(Ct))).Value);
        factory.Clock.Advance(TimeSpan.FromSeconds(65));
        Assert.Equal(1, (await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().ReconcileExpiredBatchAsync(Ct))).Value);
        var states = await scenario.ScopeAsync(async services => await services.GetRequiredService<ApplicationDbContext>().Set<AiAssistantTurn>()
            .Where(turn => turn.OwnerUserId == actor).Select(turn => new { turn.RequestId, turn.State, turn.ProviderSteps }).ToArrayAsync(Ct));
        Assert.Equal(7, states.Count(turn => turn.State == AiAssistantTurnState.Completed));
        Assert.Single(states, turn => turn.State == AiAssistantTurnState.Uncertain && turn.ProviderSteps == 1);
        var expired = states.Where(turn => turn.State == AiAssistantTurnState.Expired).ToArray();
        Assert.Equal(batch[^4..].Order(), expired.Select(turn => turn.RequestId).Order());
        Assert.All(expired, turn => Assert.Equal(0, turn.ProviderSteps));
        var day = DateOnly.FromDateTime(factory.Clock.GetUtcNow().UtcDateTime);
        Task<AiAssistantBudgetCounts> CountAsync() => scenario.ScopeAsync(services =>
            services.GetRequiredService<IAiAssistantRepository>().CountBudgetAsync(actor, day, "home", false, false, Ct));
        Assert.Equal((8, 8), ((await CountAsync()).ActorAccepted, (await CountAsync()).InstallationAccepted));
        // The id of an expired turn still answers with that turn, without taking the quota again.
        var replayed = await scenario.SubmitAsync(batch[^1], "Fictitious long question");
        Assert.Equal(("expired", batch[^1]), (replayed.GetProperty("state").GetString(), replayed.GetProperty("requestId").GetGuid()));
        Assert.Equal(8, (await CountAsync()).ActorAccepted);
        // The quota they gave back accepts four new turns, and then it is spent.
        for (var again = 0; again < 4; again++) await scenario.SubmitAsync(Guid.NewGuid(), "Fictitious question after the batch");
        using var spent = await scenario.Client.PostAsJsonAsync("/api/ai/assistant/turns", new
        {
            requestId = Guid.NewGuid(), question = "Fictitious question past the quota", expectedConnectionRevision = scenario.ConnectionRevision,
        }, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, spent.StatusCode);
        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, (await spent.ReadJsonAsync()).GetProperty("code").GetString());
    }
}
