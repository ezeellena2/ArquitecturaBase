using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I4: a turn sealed by a newer version with a value this one does not know stays pending after the claim, which takes the next one; its
// lookup answers that it may pass, never «not found»; and if no version takes it, it expires without an attempt and gives its quota back.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantUnknownSealTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_turn_with_a_future_value_stays_pending_answers_that_it_may_pass_and_expires_without_spending_quota()
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
        var context = new AssistantChannelContext("fixture-version", actor, actor, "en", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        var future = new AssistantOperationRequest(Guid.NewGuid(), context, "Fictitious visit", now, now.AddHours(1));
        var known = future with { RequestId = Guid.NewGuid(), Context = context with { ChannelConversationId = Guid.NewGuid() } };
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(future, Ct))).IsSuccess);
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(known, Ct))).IsSuccess);
        // A newer version sealed the oldest one with a terminal this one does not know.
        await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlAsync($"""
            UPDATE "AiAssistantTurns" SET "ExecutionProfileJson" = replace("ExecutionProfileJson", '"assistant_proposal","version":1', '"assistant_proposal","version":9')
            WHERE "RequestId" = {future.RequestId}
            """, Ct));

        var claimed = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct));
        var next = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct));
        var found = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(context, future.RequestId, Ct));
        var canceled = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().CancelAsync(context, future.RequestId, Ct));

        Assert.Equal(known.RequestId, claimed.Value?.RequestId);
        Assert.Null(next.Value);
        Assert.Equal((AiAssistantErrors.UnknownVersion, AiAssistantErrors.UnknownVersion), (found.Error, canceled.Error));
        Assert.Equal(AiAssistantTurnState.Pending, await StateAsync(future.RequestId));

        // Nobody that knows it takes it: it expires without an attempt and its quota comes back (I1).
        factory.Clock.Advance(TimeSpan.FromSeconds(121));
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().ReconcileExpiredBatchAsync(Ct))).IsSuccess);
        Assert.Equal(AiAssistantTurnState.Expired, await StateAsync(future.RequestId));
        var day = DateOnly.FromDateTime(now);
        var counts = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantRepository>()
            .CountBudgetAsync(actor, day, context.ChannelId, false, false, Ct));
        var spent = await scenario.ScopeAsync(async services => await services.GetRequiredService<ApplicationDbContext>().Set<AiAssistantTurn>()
            .CountAsync(turn => turn.OwnerUserId == actor && turn.ChannelId == context.ChannelId && turn.State != AiAssistantTurnState.Expired, Ct));
        Assert.Equal(spent, counts.ActorAccepted);

        Task<AiAssistantTurnState> StateAsync(Guid requestId) => scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>()
            .Set<AiAssistantTurn>().Where(turn => turn.RequestId == requestId).Select(turn => turn.State).SingleAsync(Ct));
    }
}
