using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record LlmConversationRequest(AiProvider Provider, string Model, string ApiKey, string SystemPrompt,
    IReadOnlyList<LlmConversationMessage> Messages, IReadOnlyList<LlmToolDefinition> Tools,
    IReadOnlyList<LlmToolResult> ToolResults, string? Continuation, int MaxOutputTokens,
    int MaxResponseBytes, int MaxContinuationBytes)
{
    public AssistantAudioInput? Audio { get; init; }
    public int AudioMaximumBytes { get; init; }
    /// <summary>ReadDocument: the private document, the bytes its sealed profile takes and whether it asks for a low reasoning effort.</summary>
    public AssistantDocumentInput? Document { get; init; }
    public int DocumentMaximumBytes { get; init; }
    public bool LowReasoningEffort { get; init; }
    public override string ToString() => "LlmConversationRequest { content and credential: [REDACTED] }";
}

public sealed record LlmConversationMessage(string Role, string Text)
{
    public override string ToString() => "LlmConversationMessage { content: [REDACTED] }";
}

public sealed record LlmToolDefinition(string Name, string Description, string ParametersJson);
public sealed record LlmToolResult(string CallId, string Name, string ResultJson)
{
    public override string ToString() => "LlmToolResult { content: [REDACTED] }";
}
