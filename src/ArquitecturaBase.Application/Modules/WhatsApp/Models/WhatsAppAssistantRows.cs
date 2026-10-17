using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppAssistantSourceRow(Guid Id, Guid ContactId, string Body, DateTime OccurredAtUtc, DateTime ContentExpiresAtUtc)
{
    public override string ToString() => "WhatsAppAssistantSourceRow { Content = [REDACTED] }";
}
public sealed record WhatsAppAssistantReferenceRow(Guid RequestId, Guid ContactId, Guid SourceMessageId, Guid ActorUserId, Guid SubjectUserId,
    WhatsAppAssistantRequestKind Kind, long ModeRevision, long LinkRevision, string ContextJson, DateTime ContentExpiresAtUtc,
    WhatsAppAssistantRequestState State, Guid? ProposalId, Guid? DefinitionId, Guid? OutgoingId)
{
    public override string ToString() => "WhatsAppAssistantReferenceRow { Context = [REDACTED] }";
}
