using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AiAssistantProposalProviderDouble : ILlmConversationClient
{
    public int Calls { get; private set; }
    /// <summary>I5: the first step reads <see cref="ReadFirst"/> before proposing; every request is kept to see what came back.</summary>
    public string? ReadFirst { get; init; }
    public List<LlmConversationRequest> Requests { get; } = [];
    public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add(request);
        if (ReadFirst is { } read && Calls == 1)
            return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse("tools", [new("read", read, "{}")], "opaque-test-continuation", new(10, 2)));
        var step = ReadFirst is null ? Calls : Calls - 1;
        if (step == 1)
            return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse("tools", [new("proposal", "fixture.proposal", "{}")], "opaque-test-continuation", new(10, 2)));
        Assert.Equal(2, step);
        using var json = JsonDocument.Parse(Assert.Single(request.ToolResults).ResultJson);
        var proposalId = json.RootElement.GetProperty("proposalId").GetGuid();
        return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse("tools",
            [new("terminal", "assistant_proposal", JsonSerializer.Serialize(new { proposalId }))], null, new(4, 1)));
    }
}
