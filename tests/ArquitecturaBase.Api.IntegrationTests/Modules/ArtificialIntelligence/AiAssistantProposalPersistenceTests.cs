using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantProposalPersistenceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Home_acceptance_seals_its_profile_and_recovery_never_uses_a_new_version_silently()
    {
        AiAssistantOptions? configuredOptions = null;
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary", services =>
        {
            services.PostConfigure<AiAssistantOptions>(options =>
            {
                options.ModelProfiles = options.ModelProfiles.Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                    .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray();
                configuredOptions = options;
            });
        });
        var accepted = await scenario.SubmitAsync(Guid.NewGuid(), "How many active users are there?");
        var requestId = accepted.GetProperty("requestId").GetGuid();
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var captured = await scenario.ScopeAsync(async services =>
        {
            var row = await services.GetRequiredService<IAiAssistantReader>().FindTurnByRequestAsync(actor, requestId, Ct);
            Assert.NotNull(row);
            Assert.NotNull(row.ExecutionProfileJson);
            var profile = JsonSerializer.Deserialize<AiAssistantExecutionProfileSnapshot>(row.ExecutionProfileJson, AiAssistantResponsePolicy.JsonOptions)!;
            Assert.Equal(AiAssistantTurnState.Pending, row.State);
            Assert.Equal("home", profile.ChannelContext.ChannelId);
            Assert.Equal(actor, profile.ChannelContext.ActorUserId);
            Assert.Null(profile.ChannelContext.SubjectUserId);
            Assert.Equal(1, profile.ModelProfileVersion);
            Assert.Equal("fixture-model", profile.ModelProfileId);
            return row.ExecutionProfileJson;
        });
        Assert.NotNull(configuredOptions);
        Assert.Single(configuredOptions.ModelProfiles);
        // Change the current registered profile without rewriting the already accepted durable row.
        configuredOptions.ModelProfiles[0] = configuredOptions.ModelProfiles[0] with { Version = 2 };
        // The failed claim is persisted without handing an executable claim to the coordinator.
        Assert.False(await scenario.ProcessNextAsync());
        Assert.Empty(scenario.Handler.Requests);
        await scenario.ScopeAsync(async services =>
        {
            var row = await services.GetRequiredService<IAiAssistantReader>().FindTurnByRequestAsync(actor, requestId, Ct);
            Assert.NotNull(row);
            Assert.Equal(AiAssistantTurnState.Failed, row.State);
            Assert.Equal(captured, row.ExecutionProfileJson);
            Assert.Null(row.ProtectedAnswer);
            return true;
        });
    }

    [Fact]
    public async Task A_proposal_uses_the_real_engine_protected_progress_and_a_fresh_scope_for_reading_without_reexecution()
    {
        var source = new RoundtripAssistantSource();
        var provider = new AiAssistantProposalProviderDouble();
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "fixture.proposal", services =>
        {
            services.AddSingleton<IAssistantCapabilitySource>(source);
            services.RemoveAll<ILlmConversationClient>();
            services.AddSingleton<ILlmConversationClient>(provider);
            services.PostConfigure<AiAssistantOptions>(options => options.ModelProfiles = options.ModelProfiles
                .Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray());
        });
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var requestId = Guid.NewGuid();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var context = new AssistantChannelContext("fixture-proposal", actor, actor, "en", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        var profile = new AiAssistantExecutionProfileSnapshot("fixture-model", 1, "capabilities-v1", 1, context);
        var conversation = AiConversation.Create(actor, now, context.ChannelId);
        var turn = AiAssistantTurn.Create(actor, conversation.Id, requestId, 1, "not-yet-protected", "en", "UTC",
            AiProvider.OpenAI, "fictional-model", scenario.ConnectionRevision, now, now.AddDays(30), conversation.ChannelId);
        await scenario.ScopeAsync(async services =>
        {
            var protector = services.GetRequiredService<IAiConversationProtector>();
            turn.ProtectQuestion(protector.Protect(turn.OwnerUserId, conversation.Id, turn.Id, "question", turn.PayloadRevision, "Fictitious registration intent"));
            turn.SealExecutionProfile(JsonSerializer.Serialize(profile, AiAssistantResponsePolicy.JsonOptions));
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Set<AiConversation>().Add(conversation);
            db.Set<AiAssistantTurn>().Add(turn);
            db.Set<AiAssistantRequestReceipt>().Add(AiAssistantRequestReceipt.Create(actor, requestId, "fixture-fingerprint", "fixture-key",
                conversation.Id, turn.Id, now, now.AddDays(30), conversation.ChannelId, false));
            await db.SaveChangesAsync(Ct);
            return true;
        });
        Assert.True(await scenario.ProcessNextAsync());
        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, source.Executions);
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Set<AiAssistantTurn>().SingleAsync(candidate => candidate.Id == turn.Id, Ct);
            Assert.Equal(AiAssistantTurnState.Completed, stored.State);
            Assert.NotNull(stored.ProtectedAnswer);
            Assert.DoesNotContain(source.PrivateText, stored.ProtectedAnswer, StringComparison.Ordinal);
            var tool = await db.Set<AiAssistantToolExecution>().SingleAsync(candidate => candidate.TurnId == turn.Id, Ct);
            Assert.Equal("fixture.proposal", tool.ToolId);
            Assert.Equal(1, tool.Version);
            Assert.DoesNotContain(source.PrivateText, tool.ProtectedResult!, StringComparison.Ordinal);
            return true;
        });
        await scenario.ScopeAsync(async services =>
        {
            var row = await services.GetRequiredService<IAiAssistantReader>().FindTurnByRequestAsync(actor, requestId, Ct);
            Assert.NotNull(row);
            var result = services.GetRequiredService<AiAssistantResponsePolicy>().Map(row, true, 1);
            Assert.True(result.IsSuccess);
            Assert.Null(result.Value.Answer);
            Assert.NotNull(result.Value.ObservedProposal);
            var payload = Assert.IsType<RoundtripAssistantPayload>(result.Value.ObservedProposal.Payload);
            Assert.Equal(source.ResourceId, payload.ResourceId);
            Assert.Equal(source.PrivateText, payload.PrivateText);
            Assert.Equal("fixture-proposal", JsonSerializer.Deserialize<AiAssistantExecutionProfileSnapshot>(row.ExecutionProfileJson!, AiAssistantResponsePolicy.JsonOptions)!.ChannelContext.ChannelId);
            return true;
        });
        using (var homeList = await scenario.Client.GetAsync("/api/ai/assistant/conversations", Ct))
        {
            Assert.Equal(HttpStatusCode.OK, homeList.StatusCode);
            Assert.DoesNotContain((await homeList.ReadJsonAsync()).GetProperty("items").EnumerateArray(), item => item.GetProperty("conversationId").GetGuid() == conversation.Id);
        }
        using (var homeDetail = await scenario.Client.GetAsync($"/api/ai/assistant/conversations/{conversation.Id}", Ct)) Assert.Equal(HttpStatusCode.NotFound, homeDetail.StatusCode);
        using (var homeTurn = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct)) Assert.Equal(HttpStatusCode.NotFound, homeTurn.StatusCode);
        Assert.True(source.ParseCalls > 0);
        Assert.False(await scenario.ProcessNextAsync());
        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, source.Executions);
    }
    [Fact]
    public async Task The_neutral_operation_port_accepts_executes_and_recovers_provenance_with_the_owners_shorter_retention()
    {
        var source = new RoundtripAssistantSource();
        var provider = new AiAssistantProposalProviderDouble();
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "fixture.proposal", services =>
        {
            services.AddSingleton<IAssistantCapabilitySource>(source);
            services.RemoveAll<ILlmConversationClient>();
            services.AddSingleton<ILlmConversationClient>(provider);
            services.PostConfigure<AiAssistantOptions>(options => options.ModelProfiles = options.ModelProfiles
                .Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray());
        });
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var occurredAtUtc = now.AddMinutes(-10);
        var context = new AssistantChannelContext("fixture-operation", actor, actor, "en", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]);
        var request = new AssistantOperationRequest(Guid.NewGuid(), context, "Fictitious request from a retained message", occurredAtUtc, now.AddHours(1));
        var accepted = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct));
        Assert.True(accepted.IsSuccess);
        Assert.Equal("pending", accepted.Value.State);
        Assert.True(await scenario.ProcessNextAsync());
        var completed = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(context, request.RequestId, Ct));
        Assert.True(completed.IsSuccess);
        Assert.Equal("completed", completed.Value.State);
        Assert.NotNull(completed.Value.Proposal);
        Assert.Equal(new RoundtripAssistantPayload(source.ResourceId, source.PrivateText), completed.Value.Proposal.Payload);
        Assert.Equal(new AssistantOperationProvenance("fixture-model", 1, "capabilities-v1", 1, occurredAtUtc, context.ChannelId), completed.Value.Provenance);
        await scenario.ScopeAsync(async services =>
        {
            var row = await services.GetRequiredService<IAiAssistantReader>().FindTurnByRequestAsync(actor, request.RequestId, Ct);
            Assert.NotNull(row);
            Assert.Equal(request.ContentExpiresAtUtc, row.ContentExpiresAtUtc);
            return true;
        });
        factory.Clock.Advance(TimeSpan.FromHours(1));
        var expired = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(context, request.RequestId, Ct));
        Assert.True(expired.IsSuccess);
        Assert.Equal("completed", expired.Value.State);
        Assert.Null(expired.Value.Proposal);
        Assert.Equal(completed.Value.Provenance, expired.Value.Provenance);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, source.Executions);
    }

    // Stage 2: the lookup also finds a proposal of the actor's own conversation in a channel (the assistant mode with the actor as its
    // Subject), with the instant its seal keeps and the spreadsheet its turn sealed in PostgreSQL.
    [Fact]
    public async Task The_lookup_finds_a_proposal_of_the_actors_own_channel_conversation_with_its_spreadsheet()
    {
        var source = new RoundtripAssistantSource();
        var provider = new AiAssistantProposalProviderDouble();
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "fixture.proposal", services =>
        {
            services.AddSingleton<IAssistantCapabilitySource>(source);
            services.RemoveAll<ILlmConversationClient>();
            services.AddSingleton<ILlmConversationClient>(provider);
            services.PostConfigure<AiAssistantOptions>(options => options.ModelProfiles = options.ModelProfiles
                .Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray());
        });
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var occurredAtUtc = now.AddMinutes(-10);
        var context = new AssistantChannelContext("fixture-operation", actor, actor, "es", "UTC", new([], []),
            [new("fixture.proposal", 1)], [new("assistant_proposal", 1)]) { InteractionMode = AssistantInteractionMode.Assistant };
        const string sha256 = "8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa4";
        var sheet = new StructuredSpreadsheet("clientes.csv", StructuredSpreadsheetKind.Csv, "text/csv", 64, sha256, "", 2, 2,
            [new(1, new Dictionary<string, string> { ["A"] = "CUIT", ["B"] = "Razón social" })]);
        var attachment = new AssistantAttachmentInput("fixture/attachments/" + Guid.NewGuid().ToString("N"), "clientes.csv", "text/csv", 64, sha256,
            now.AddHours(2), sheet);
        var request = new AssistantOperationRequest(Guid.NewGuid(), context, "Armame un catálogo con esta planilla", occurredAtUtc, now.AddHours(1))
            { Attachment = attachment };
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct))).IsSuccess);
        Assert.True(await scenario.ProcessNextAsync());
        var completed = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(context, request.RequestId, Ct));
        var proposalId = completed.Value.Proposal!.Id;
        var found = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantProposalLookup>()
            .FindAsync(actor, request.RequestId, proposalId, ["fixture.proposal"], Ct));
        Assert.True(found.IsSuccess);
        Assert.Equal(new AssistantAttachmentDescriptor(attachment.StorageKey, "clientes.csv", "text/csv", 64, sha256), found.Value.Attachment);
        Assert.Equal(completed.Value.Provenance, found.Value.Provenance);
        Assert.Equal(new RoundtripAssistantPayload(source.ResourceId, source.PrivateText), found.Value.Proposal.Payload);
        Assert.True(found.Value.IsCurrent);
        var stranger = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantProposalLookup>()
            .FindAsync(Guid.NewGuid(), request.RequestId, proposalId, ["fixture.proposal"], Ct));
        Assert.True(stranger.IsFailure);
    }

}
