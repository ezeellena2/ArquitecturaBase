using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantChannelReservationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_real_capacity_conflict_can_retry_the_same_uuid_after_an_existing_reservation_is_canceled()
    {
        await using var scenario=await AiAssistantExecutionScenario.CreateAsync(factory,AiProvider.OpenAI,"fixture.proposal",services=>
        {
            services.AddSingleton<IAssistantCapabilitySource>(new RoundtripAssistantSource());
            services.PostConfigure<AiAssistantOptions>(options=>options.Limits=options.Limits with {PendingCapacity=1});
        });
        var actor=Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now=factory.Clock.GetUtcNow().UtcDateTime;
        var context=new AssistantChannelContext("capacity-first",actor,actor,"en","UTC",new([],[]),[new("fixture.proposal",1)],[new("assistant_proposal",1)]);
        var first=new AssistantOperationRequest(Guid.NewGuid(),context,"Fictitious first request",now,now.AddHours(1));
        var waiting=first with {RequestId=Guid.NewGuid(),Context=context with {ChannelId="capacity-second"}};
        try
        {
            Assert.True((await scenario.ScopeAsync(services=>services.GetRequiredService<IAssistantOperations>().SubmitAsync(first,Ct))).IsSuccess);
            var limited=await scenario.ScopeAsync(services=>services.GetRequiredService<IAssistantOperations>().SubmitAsync(waiting,Ct));
            Assert.True(limited.IsFailure);Assert.Equal(AiAssistantErrors.CapacityLimited(1).Code,limited.Error.Code);
            Assert.True((await scenario.ScopeAsync(services=>services.GetRequiredService<IAssistantOperations>().CancelAsync(first.Context,first.RequestId,Ct))).IsSuccess);
            var retry=await scenario.ScopeAsync(services=>services.GetRequiredService<IAssistantOperations>().SubmitAsync(waiting,Ct));
            Assert.True(retry.IsSuccess);Assert.Equal(waiting.RequestId,retry.Value.RequestId);Assert.Empty(scenario.Handler.Requests);
        }
        finally
        {
            await scenario.ScopeAsync(services=>services.GetRequiredService<IAssistantOperations>().CancelAsync(first.Context,first.RequestId,Ct));
            await scenario.ScopeAsync(services=>services.GetRequiredService<IAssistantOperations>().CancelAsync(waiting.Context,waiting.RequestId,Ct));
        }
    }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audio_does_not_reserve_the_actor_channel_while_interpretation_remains_exclusive_across_hosts(bool audioFirst)
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "fixture.proposal", services =>
        {
            services.AddSingleton<IAssistantCapabilitySource>(new RoundtripAssistantSource());
            services.PostConfigure<AiAssistantOptions>(options =>
                options.AudioModelProfiles = [new(AiProvider.OpenAI, "fictional-audio", "fixture-audio", 1, ["audio/ogg"], 25000000)]);
        });
        await using var secondHost = scenario.CreateAdditionalHost();
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var context = new AssistantChannelContext("whatsapp-manual", actor, actor, "en", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        var text = new AssistantOperationRequest(Guid.NewGuid(), context, "Fictitious visit", now, now.AddHours(1));
        var audio = text with { RequestId = Guid.NewGuid(), Text = "", Context = context with { SubjectUserId = null, AllowedCapabilities = [], AllowedTerminals = [] },
            Audio = new("fictional/private-audio", "audio.ogg", "audio/ogg", 42, new string('a', 64), now.AddHours(1)) };
        var additional = audio with { RequestId = Guid.NewGuid(), Audio = audio.Audio! with { StorageKey = "fictional/another-private-audio" } };
        var requests = audioFirst ? new[] { audio, text } : new[] { text, audio };
        var accepted = new List<AssistantOperationRequest>();
        try
        {
            var first = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(requests[0], Ct));
            Assert.True(first.IsSuccess, first.IsFailure ? first.Error.Code : null);
            accepted.Add(requests[0]);
            await using var scope = secondHost.Services.CreateAsyncScope();
            var second = await scope.ServiceProvider.GetRequiredService<IAssistantOperations>().SubmitAsync(requests[1], Ct);
            Assert.True(second.IsSuccess, second.IsFailure ? second.Error.Code : null);
            accepted.Add(requests[1]);
            var third = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(additional, Ct));
            Assert.True(third.IsSuccess, third.IsFailure ? third.Error.Code : null);
            accepted.Add(additional);
            var reservation = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantRepository>()
                .GetActiveForActorChannelAsync(actor, context.ChannelId, Ct));
            Assert.NotNull(reservation);
            Assert.Equal(text.RequestId, reservation.RequestId);
            var blocked = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>()
                .SubmitAsync(text with { RequestId = Guid.NewGuid() }, Ct));
            Assert.Equal(AiAssistantErrors.ActiveTurn.Code, blocked.Error.Code);
            Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>()
                .SubmitAsync(audio, Ct))).IsSuccess);
            var hidden = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>()
                .GetAsync(audio.Context with { SubjectUserId = actor }, audio.RequestId, Ct));
            Assert.Equal(AiAssistantErrors.NotFound.Code, hidden.Error.Code);
            Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>()
                .CancelAsync(audio.Context, audio.RequestId, Ct))).IsSuccess);
            var pending = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>()
                .GetAsync(additional.Context, additional.RequestId, Ct));
            Assert.True(pending.IsSuccess);
            Assert.Equal("pending", pending.Value.State);
            await scenario.ScopeAsync(async services =>
            {
                var db = services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>();
                Assert.Equal(3, await db.Set<AiAssistantRequestReceipt>().CountAsync(receipt => receipt.OwnerUserId == actor, Ct));
                return true;
            });
            Assert.Empty(scenario.Handler.Requests);
        }
        finally
        {
            foreach (var request in accepted)
                await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().CancelAsync(request.Context, request.RequestId, Ct));
        }
    }
}
