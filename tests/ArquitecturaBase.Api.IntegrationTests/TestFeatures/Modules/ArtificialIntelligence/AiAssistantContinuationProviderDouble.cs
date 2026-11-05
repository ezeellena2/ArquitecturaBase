using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

// Each operation proposes and then selects what it observed, as many times as asked; it keeps every request to read its history.
internal sealed class AiAssistantContinuationProviderDouble : ILlmConversationClient
{
    public List<LlmConversationRequest> Requests { get; } = [];

    public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add(request);
        if (request.ToolResults.Count == 0)
            return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse("tools", [new("proposal", "fixture.proposal", "{}")], "opaque-test-continuation", new(10, 2)));
        using var json = JsonDocument.Parse(Assert.Single(request.ToolResults).ResultJson);
        var proposalId = json.RootElement.GetProperty("proposalId").GetGuid();
        return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse("tools",
            [new("terminal", "assistant_proposal", JsonSerializer.Serialize(new { proposalId }))], null, new(4, 1)));
    }
}
