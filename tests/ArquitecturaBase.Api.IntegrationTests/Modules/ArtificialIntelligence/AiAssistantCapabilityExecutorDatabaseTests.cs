using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.SystemQueries;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

/// <summary>
/// I5 with the real engine, executor and PostgreSQL: a source whose database fails is never blamed on the provider. A read, asked by
/// the model or preloaded (WhatsApp preloads its reads), is unavailable and the turn goes on; a proposal ends it with its own code.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantCapabilityExecutorDatabaseTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("preload")]
    [InlineData("read")]
    [InlineData("proposal")]
    public async Task A_database_failure_of_a_source_is_unavailable_for_a_read_and_ends_a_proposal_with_its_own_code(string where)
    {
        var read = new FailingReadAssistantSource();
        var proposal = new RoundtripAssistantSource();
        if (where == "proposal") proposal.BeforeReturn = _ => throw new FictionalDbException();
        var provider = new AiAssistantProposalProviderDouble { ReadFirst = where == "read" ? FailingReadAssistantSource.Reference.Id : null };
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "fixture.proposal", services =>
        {
            services.AddSingleton<IAssistantCapabilitySource>(read);
            services.AddSingleton<IAssistantCapabilitySource>(proposal);
            services.RemoveAll<ILlmConversationClient>();
            services.AddSingleton<ILlmConversationClient>(provider);
            services.PostConfigure<AiAssistantOptions>(options => options.ModelProfiles = options.ModelProfiles
                .Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray());
        });
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        AssistantCapabilityReference[] tools = where == "read"
            ? [FailingReadAssistantSource.Reference, new("fixture.proposal", 1)] : [new("fixture.proposal", 1)];
        var context = new AssistantChannelContext("fixture-database", actor, actor, "en", "UTC", new([], []), tools, [new("assistant_proposal", 1)])
        {
            PreloadedCapabilities = where == "preload" ? [FailingReadAssistantSource.Reference] : null,
        };
        var request = new AssistantOperationRequest(Guid.NewGuid(), context, "Fictitious request while the database fails", now, now.AddHours(1));
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct))).IsSuccess);

        Assert.True(await scenario.ProcessNextAsync());

        var outcome = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAsync(context, request.RequestId, Ct));
        Assert.True(outcome.IsSuccess);
        Assert.NotEqual(AiAssistantErrors.ProviderUnavailable.Code, outcome.Value.FailureCode);
        if (where == "proposal")
        {
            Assert.Equal(("failed", SystemQueryErrors.UnavailableCode), (outcome.Value.State, outcome.Value.FailureCode));
            Assert.Equal((1, 1), (provider.Calls, proposal.Executions));
            return;
        }
        Assert.Equal(("completed", (string?)null), (outcome.Value.State, outcome.Value.FailureCode));
        Assert.NotNull(outcome.Value.Proposal);
        Assert.Equal(1, read.Executions);
        // The model sees the read as unavailable, with the code and without the failure's detail.
        var seen = where == "preload" ? provider.Requests[0].Messages[0].Text : Assert.Single(provider.Requests[1].ToolResults).ResultJson;
        Assert.Contains("unavailable", seen, StringComparison.Ordinal);
        Assert.Contains(SystemQueryErrors.UnavailableCode, seen, StringComparison.Ordinal);
        Assert.DoesNotContain("private", seen, StringComparison.Ordinal);
    }
}
