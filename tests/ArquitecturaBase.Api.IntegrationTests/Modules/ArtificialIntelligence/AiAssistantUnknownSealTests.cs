using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I4: a turn sealed by a newer version with a value this one does not know stays pending after the claim, which takes the next one; its
// lookup answers that it may pass, never «not found»; and if no version takes it, it expires without an attempt and gives its quota back.
// Once expired, its lookup answers that state in clear (I4, review).
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
            services.PostConfigure<AiAssistantOptions>(options =>
            {
                options.ModelProfiles = options.ModelProfiles.Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                    .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray();
                // The exact quota: two acceptances of the channel, so a third goes only when one of them gives its quota back.
                options.Limits = options.Limits with { ActorDailyQuota = 2 };
            });
        });
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var context = new AssistantChannelContext("fixture-version", actor, actor, "en", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        var future = new AssistantOperationRequest(Guid.NewGuid(), context, "Fictitious visit", now, now.AddHours(1));
        var known = future with { RequestId = Guid.NewGuid(), Context = context with { ChannelConversationId = Guid.NewGuid() } };
        var third = future with { RequestId = Guid.NewGuid(), Context = context with { ChannelConversationId = Guid.NewGuid() } };
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

        Assert.Equal(known.RequestId, claimed.Value?.RequestId);
        Assert.Null(next.Value);
        Assert.Equal(AiAssistantErrors.UnknownVersion, found.Error);
        Assert.Equal(AiAssistantTurnState.Pending, await StateAsync(future.RequestId));
        // The known one reserves a step, so it keeps its quota when it ends uncertain; with both counted, a third does not go.
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().BeginProviderStepAsync(claimed.Value!, Ct))).Value);
        Assert.Equal("Ai.Assistant.QuotaExceeded", (await SubmitAsync(third)).Error.Code);

        // Nobody that knows it takes it: it expires without an attempt and its quota comes back (I1), so the third goes now.
        factory.Clock.Advance(TimeSpan.FromSeconds(121));
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().ReconcileExpiredBatchAsync(Ct))).IsSuccess);
        Assert.Equal(AiAssistantTurnState.Expired, await StateAsync(future.RequestId));
        Assert.Equal(AiAssistantTurnState.Uncertain, await StateAsync(known.RequestId));
        var accepted = await SubmitAsync(third);
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);

        // Expired, it answers its state in clear, without the seal; cancelling it changes nothing.
        var expired = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(context, future.RequestId, Ct));
        Assert.True(expired.IsSuccess, expired.IsFailure ? expired.Error.Code : null);
        Assert.Equal(("expired", AiAssistantErrors.Expired.Code), (expired.Value.State, expired.Value.FailureCode));
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().CancelAsync(context, future.RequestId, Ct))).IsSuccess);
        Assert.Equal(AiAssistantTurnState.Expired, await StateAsync(future.RequestId));

        Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request) =>
            scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct));

        Task<AiAssistantTurnState> StateAsync(Guid requestId) => scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>()
            .Set<AiAssistantTurn>().Where(turn => turn.RequestId == requestId).Select(turn => turn.State).SingleAsync(Ct));
    }
}
