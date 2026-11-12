using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

internal sealed record FormRecordCaptureOriginPreparation(StructuredRecordCaptureOrigin Origin,
    IReadOnlyList<Guid> ChangedFieldIds,IReadOnlyList<FormRecordProposedChoice> ProposedChoices)
{
    /// <summary>The received document of the origin (task 7), tracked and checked under its lock, which the capture confirms.</summary>
    public ArquitecturaBase.Domain.Modules.BusinessForms.Documents.FormReceivedDocument? Document { get; init; }
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
