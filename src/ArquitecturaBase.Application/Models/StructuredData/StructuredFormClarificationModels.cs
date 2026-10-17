using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormClarificationRequest(Guid RecipientUserId, string Culture, AssistantObservedProposal Proposal)
{ public override string ToString() => "StructuredFormClarificationRequest { Content = [REDACTED] }"; }
public sealed record StructuredFormClarificationResponse(string Question, IReadOnlyList<StructuredFormClarificationChoice> Choices)
{ public override string ToString() => "StructuredFormClarificationResponse { Content = [REDACTED] }"; }
public sealed record StructuredFormClarificationChoice(Guid DefinitionId, Guid VersionId, string Label)
{ public override string ToString() => "StructuredFormClarificationChoice { Content = [REDACTED] }"; }
