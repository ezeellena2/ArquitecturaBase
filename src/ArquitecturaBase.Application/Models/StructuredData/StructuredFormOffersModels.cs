using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormOffersPayload(Guid SubjectUserId, string Culture,
    IReadOnlyList<StructuredFormOfferReference> Offers, IReadOnlyList<Guid> DeclinedDefinitionIds) : AssistantCapabilityPayload;

public sealed record StructuredFormOfferReference(Guid RequestId, Guid DefinitionId, Guid VersionId, string Name)
{
    public override string ToString() => "StructuredFormOfferReference { Presentation = [REDACTED] }";
}
