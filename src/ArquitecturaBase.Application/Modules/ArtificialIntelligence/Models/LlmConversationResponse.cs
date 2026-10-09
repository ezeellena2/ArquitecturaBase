namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record LlmConversationResponse(string State, IReadOnlyList<LlmToolCall> ToolCalls,
    string? Continuation, LlmConversationUsage? Usage)
{
    public override string ToString() => "LlmConversationResponse { content: [REDACTED] }";
}

public sealed record LlmToolCall(string CallId, string Name, string ArgumentsJson)
{
    public override string ToString() => "LlmToolCall { arguments: [REDACTED] }";
}

public sealed record LlmConversationUsage(long? InputTokens, long? OutputTokens);
