using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts.Assistant;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitAiAssistantTurnHttpRequest(Guid RequestId, Guid? ConversationId,
    long? ExpectedRevision, long ExpectedConnectionRevision, string Question, Guid? AudioRequestId = null)
{
    public override string ToString() => "SubmitAiAssistantTurnHttpRequest { question: [REDACTED] }";
}
