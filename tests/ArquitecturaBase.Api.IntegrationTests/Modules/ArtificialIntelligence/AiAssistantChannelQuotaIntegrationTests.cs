using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
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
                Assert.Equal(1, (await repository.CountBudgetAsync(actor, DateOnly.FromDateTime(now), "other-channel", audio, Ct)).ActorAccepted);
                Assert.Equal(2, (await repository.CountBudgetAsync(actor, DateOnly.FromDateTime(now), "known-channel", audio, Ct)).ActorAccepted);
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
}
