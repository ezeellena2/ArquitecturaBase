using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantChannelReservationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Independent_channels_and_home_conversations_keep_distinct_reservations_and_cancellation_releases_only_its_channel()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "fixture.proposal", services =>
        {
            services.AddSingleton<IAssistantCapabilitySource>(new RoundtripAssistantSource());
            services.PostConfigure<AiAssistantOptions>(options => options.ModelProfiles = options.ModelProfiles
                .Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray());
        });
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var firstContext = new AssistantChannelContext("fixture-first", actor, actor, "en", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        var secondContext = firstContext with { ChannelId = "fixture-second" };
        var first = new AssistantOperationRequest(Guid.NewGuid(), firstContext, "Fictitious first channel request", now, now.AddHours(1));
        var second = first with { RequestId = Guid.NewGuid(), Context = secondContext };
        var waiting = first with { RequestId = Guid.NewGuid() };
        var accepted = new List<AssistantOperationRequest>();
        var home = new List<Guid>();
        try
        {
            foreach (var request in new[] { first, second })
            {
                var result = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct));
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
                accepted.Add(request);
            }
            for (var index = 0; index < 2; index++)
            {
                var requestId = Guid.NewGuid();
                await scenario.SubmitAsync(requestId, "Fictitious question in another Home conversation");
                home.Add(requestId);
            }
            var blocked = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(waiting, Ct));
            Assert.True(blocked.IsFailure);
            Assert.Equal(AiAssistantErrors.ActiveTurn.Code, blocked.Error.Code);
            await scenario.ScopeAsync(async services =>
            {
                var db = services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>();
                var turns = await db.Set<AiAssistantTurn>().Where(turn => turn.OwnerUserId == actor)
                    .Select(turn => new { turn.ChannelId, turn.RequestId }).ToArrayAsync(Ct);
                Assert.Equal(4, turns.Length);
                Assert.Equal(2, turns.Count(turn => turn.ChannelId == "home"));
                Assert.Contains(turns, turn => turn.ChannelId == firstContext.ChannelId && turn.RequestId == first.RequestId);
                Assert.Contains(turns, turn => turn.ChannelId == secondContext.ChannelId && turn.RequestId == second.RequestId);
                Assert.DoesNotContain(turns, turn => turn.RequestId == waiting.RequestId);
                return true;
            });
            Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>()
                .CancelAsync(firstContext, first.RequestId, Ct))).IsSuccess);
            var retried = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(waiting, Ct));
            Assert.True(retried.IsSuccess, retried.IsFailure ? retried.Error.Code : null);
            accepted.Add(waiting);
            var otherChannel = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>()
                .GetAsync(secondContext, second.RequestId, Ct));
            Assert.True(otherChannel.IsSuccess);
            Assert.Equal("pending", otherChannel.Value.State);
        }
        finally
        {
            foreach (var request in accepted)
                await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().CancelAsync(request.Context, request.RequestId, Ct));
            foreach (var requestId in home)
            {
                using var canceled = await scenario.Client.PostAsync($"/api/ai/assistant/turns/{requestId}/cancel", null, Ct);
                Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
            }
        }
    }
}
