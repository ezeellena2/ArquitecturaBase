using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I4: a deployment raises the version of a module's capability and keeps the earlier one registered. What the earlier version sealed is
// still found, replayed, taken by the worker and cancelled with the context the module builds after the raise: a received document's
// extraction (Formularios) and a turn of the person's conversation (WhatsApp), with the ids and the shape each one sends, without importing
// either module. The raise is simulated with a fictitious version 2 next to the module's real version 1.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantSealedVersionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_forms_extraction_sealed_with_the_earlier_version_of_its_capability_is_recovered_after_raising_it()
    {
        await using var scenario = await CreateAsync("forms.documents.form");
        var actor = await ActorAsync(scenario, ["formdocuments.review"]);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        // The reviewer's extraction for one fixed form, as Formularios seals it with the request.
        var context = new AssistantChannelContext("forms-documents", actor, null, "en", "UTC", new(["formdocuments.review"], []),
            [new("forms.documents.form", 1), new("forms.documents.propose-entry", 1)], [new("assistant_proposal", 1)])
            { FocusDefinitionId = Guid.NewGuid(), ChannelConversationId = Guid.NewGuid() };
        var reading = new StructuredDocument("receipt.pdf", StructuredDocumentKind.Pdf, "application/pdf", 4096, new string('d', 64), 1,
            new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, "en", "Fictitious transfer", false));
        var request = new AssistantOperationRequest(Guid.NewGuid(), context, "Load this document", now.AddMinutes(-1), now.AddHours(1))
            { Attachment = AssistantAttachmentInput.ForDocument("private/documents/" + Guid.NewGuid().ToString("N"), "receipt.pdf", now.AddDays(7), reading) };

        await AssertRecoveredAsync(scenario, request, "forms.documents.form");
    }

    [Fact]
    public async Task A_whatsapp_turn_sealed_with_the_earlier_version_of_its_capability_is_recovered_after_raising_it()
    {
        await using var scenario = await CreateAsync("whatsapp.no-reply");
        var actor = await ActorAsync(scenario, []);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        // The person's own conversation after a capture, as WhatsApp seals it with its reference.
        var context = new AssistantChannelContext("whatsapp-assistant", actor, actor, "en", "UTC", new([], []),
            [new("whatsapp.offered-forms", 1), new("whatsapp.no-reply", 1), new("whatsapp.decline-form", 1)], [new("assistant_proposal", 1)])
            { InteractionMode = AssistantInteractionMode.Assistant, SourceRecordId = Guid.NewGuid() };
        var request = new AssistantOperationRequest(Guid.NewGuid(), context, "A form response was captured.", now.AddMinutes(-1), now.AddHours(1));

        await AssertRecoveredAsync(scenario, request, "whatsapp.no-reply");
    }

    // I4, review: a turn sealed before the deployment that raises its capability is run by the copy after the raise with the version it
    // sealed, which that copy keeps registered, and closes well; the raised one (fictitious, it rejects every call) never runs.
    [Fact]
    public async Task A_turn_sealed_before_raising_its_capability_runs_the_sealed_version_after_the_raise()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId, "How many users are there?");
        await using var raised = scenario.CreateAdditionalHost(services =>
            services.AddSingleton<IAssistantCapabilitySource>(new RaisedVersionAssistantSource("users.summary", 2)));

        Assert.True(await raised.Services.GetRequiredService<AiAssistantCoordinator>().ProcessNextAsync(Ct));

        using var turn = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal("completed", (await turn.ReadJsonAsync()).GetProperty("state").GetString());
        // The tool the provider asked for answered with its evidence, and the provider closed on it.
        Assert.Equal(2, scenario.Handler.Requests.Count);
    }

    private static async Task AssertRecoveredAsync(AiAssistantExecutionScenario scenario, AssistantOperationRequest request, string capabilityId)
    {
        var accepted = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct));
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
        // After the deployment the module builds its context with the raised version.
        var raised = request.Context with { AllowedCapabilities = request.Context.AllowedCapabilities
            .Select(capability => capability.Id == capabilityId ? capability with { Version = 2 } : capability).ToArray() };
        Assert.Contains(request.Context.AllowedCapabilities, capability => capability == new AssistantCapabilityReference(capabilityId, 1));

        var found = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(raised, request.RequestId, Ct));
        var replayed = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>()
            .SubmitAsync(request with { Context = raised }, Ct));
        var claimed = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct));
        var canceled = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().CancelAsync(raised, request.RequestId, Ct));

        Assert.True(found.IsSuccess, found.IsFailure ? found.Error.Code : null);
        Assert.Equal((request.RequestId, "pending"), (found.Value.RequestId, found.Value.State));
        Assert.True(replayed.IsSuccess, replayed.IsFailure ? replayed.Error.Code : null);
        Assert.Equal(request.RequestId, replayed.Value.RequestId);
        Assert.Equal(request.RequestId, claimed.Value?.RequestId);
        Assert.True(canceled.IsSuccess, canceled.IsFailure ? canceled.Error.Code : null);
    }

    private Task<AiAssistantExecutionScenario> CreateAsync(string capabilityId) =>
        AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, capabilityId, services =>
        {
            services.AddSingleton<IAssistantCapabilitySource>(new RaisedVersionAssistantSource(capabilityId, 2));
            services.PostConfigure<AiAssistantOptions>(options => options.ModelProfiles = options.ModelProfiles
                .Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray());
        });

    // The scenario's operator, with the module's permissions its capabilities need.
    private static async Task<Guid> ActorAsync(AiAssistantExecutionScenario scenario, IReadOnlyCollection<string> permissions)
    {
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        if (permissions.Count == 0) return actor;
        var granted = await scenario.ScopeAsync(services => services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
        {
            await services.GetRequiredService<IRoleRepository>().CreateAsync("Module operator", "Integration module operator", permissions.ToArray(), ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(actor, ["Assistant operator", "Module operator"], ct);
            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct));
        Assert.True(granted.IsSuccess);
        return actor;
    }
}
