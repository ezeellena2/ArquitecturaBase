using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

/// <summary>
/// I6 with the real engine, clients and PostgreSQL: a turn says what failed at the provider, by its status, so whoever administers it
/// knows whether to replace the key, add credit or change the model; only a possible send stays uncertain, and nothing is repeated.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantProviderFailureExecutionIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(AiProvider.OpenAI, "401", "failed", "Ai.Assistant.ProviderCredentialRejected")]
    [InlineData(AiProvider.Anthropic, "402", "failed", "Ai.Assistant.ProviderLimited")]
    [InlineData(AiProvider.OpenAI, "429", "failed", "Ai.Assistant.ProviderLimited")]
    [InlineData(AiProvider.Anthropic, "404", "failed", "Ai.Assistant.ProviderModelRejected")]
    [InlineData(AiProvider.OpenAI, "refused", "failed", "Ai.Assistant.ProviderUnavailable")]
    [InlineData(AiProvider.Anthropic, "503", "uncertain", "Ai.Assistant.ProviderUncertain")]
    public async Task A_provider_failure_ends_the_turn_with_its_own_code_and_only_a_possible_send_is_uncertain(AiProvider provider, string answer,
        string state, string code)
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, provider, "users.summary");
        if (answer == "refused") scenario.Handler.RefuseConnection = true;
        else scenario.Handler.FailWith = (HttpStatusCode)int.Parse(answer, System.Globalization.CultureInfo.InvariantCulture);
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId, "How many active users are there?");

        Assert.True(await scenario.ProcessNextAsync());

        using var response = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var turn = await response.ReadJsonAsync();
        var failure = turn.GetProperty("failure");
        Assert.Equal((state, code), (turn.GetProperty("state").GetString(), failure.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(failure.GetProperty("detail").GetString()));
        Assert.DoesNotContain("private", failure.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        Assert.Equal(state == "uncertain" ? "reviewUncertainty" : "newAttempt", failure.GetProperty("recovery").GetString());
        Assert.Equal(answer == "refused" ? 0 : 1, scenario.Handler.Requests.Count);
        Assert.False(await scenario.ProcessNextAsync());
    }
}
