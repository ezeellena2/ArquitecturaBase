using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AiAssistantProposalProviderDouble : ILlmConversationClient
{
    public int Calls { get; private set; }
    public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        if (Calls == 1)
            return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse("tools", [new("proposal", "fixture.proposal", "{}")], "opaque-test-continuation", new(10, 2)));
        Assert.Equal(2, Calls);
        using var json = JsonDocument.Parse(Assert.Single(request.ToolResults).ResultJson);
        var proposalId = json.RootElement.GetProperty("proposalId").GetGuid();
        return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse("tools",
            [new("terminal", "assistant_proposal", JsonSerializer.Serialize(new { proposalId }))], null, new(4, 1)));
    }
}
