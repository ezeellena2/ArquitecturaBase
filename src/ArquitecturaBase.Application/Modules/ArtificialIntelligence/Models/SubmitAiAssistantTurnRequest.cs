namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record SubmitAiAssistantTurnRequest(Guid RequestId, Guid? ConversationId, long? ExpectedRevision,
    long ExpectedConnectionRevision, string Question, string Culture)
{
    public string NormalizedQuestion => Question.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
    public override string ToString() => "SubmitAiAssistantTurnRequest { question: [REDACTED] }";
}
