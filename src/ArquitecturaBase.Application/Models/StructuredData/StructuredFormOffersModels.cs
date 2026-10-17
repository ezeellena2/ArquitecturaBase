using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormOffersPayload(Guid SubjectUserId, string Culture,
    IReadOnlyList<StructuredFormOfferReference> Offers, IReadOnlyList<Guid> CapturedSourceRecordIds) : AssistantCapabilityPayload;

public sealed record StructuredFormOfferReference(Guid RequestId, Guid DefinitionId, Guid VersionId, string Name, bool Declined)
{
    public override string ToString() => "StructuredFormOfferReference { Presentation = [REDACTED] }";
}
