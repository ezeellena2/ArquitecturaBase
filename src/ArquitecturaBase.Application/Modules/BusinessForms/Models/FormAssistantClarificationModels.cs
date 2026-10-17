using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormAssistantClarificationReference(Guid DefinitionId, Guid VersionId, long ExpectedDefinitionRevision)
{ public override string ToString() => "FormAssistantClarificationReference { Content = [REDACTED] }"; }
public sealed record FormAssistantClarificationPayload(string State, int ReadOrdinal,
    IReadOnlyList<FormAssistantClarificationReference> Forms) : AssistantCapabilityPayload;
