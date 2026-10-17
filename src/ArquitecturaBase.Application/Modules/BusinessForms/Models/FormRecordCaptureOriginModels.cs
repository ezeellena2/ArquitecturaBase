using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

internal sealed record FormRecordCaptureOriginPreparation(StructuredRecordCaptureOrigin Origin,
    IReadOnlyList<Guid> ChangedFieldIds,IReadOnlyList<FormRecordProposedChoice> ProposedChoices)
{
    public override string ToString()=>"FormRecordCaptureOriginPreparation { Content = [REDACTED] }";
}
internal sealed record FormRecordProposedChoice(StructuredFormCatalogOffer Offer,StructuredFormCatalogOption Option)
{
    public override string ToString()=>"FormRecordProposedChoice { Content = [REDACTED] }";
}
public sealed record FormRecordCaptureOriginSnapshot(StructuredRecordCaptureOrigin Origin,IReadOnlyList<Guid> ChangedFieldIds,
    IReadOnlyList<FormRecordReferenceRow> ProposedReferences)
{
    public override string ToString()=>"FormRecordCaptureOriginSnapshot { Content = [REDACTED] }";
}
