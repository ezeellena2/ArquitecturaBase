namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormCatalogOption(Guid Id, string Text, string? Detail)
{
    public override string ToString() => "StructuredFormCatalogOption { Content = [REDACTED] }";
}

public sealed record StructuredFormCatalogOffer(Guid FieldId, string Key, string Label, Guid CatalogId,
    long Revision, string Kind, string CatalogName, IReadOnlyList<StructuredFormCatalogOption> Options)
{
    public override string ToString() => "StructuredFormCatalogOffer { Content = [REDACTED] }";
}

public sealed record StructuredFormPreparationIssue(Guid FieldId, string Label, string Code,
    string? CatalogName = null, int? InvalidOptionCount = null);
public sealed record StructuredFormPreparationResponse(IReadOnlyList<StructuredFormCatalogOffer> Catalogs,
    IReadOnlyList<StructuredFormPreparationIssue> Issues)
{
    public bool CanSend => Issues.Count == 0;
    public override string ToString() => "StructuredFormPreparationResponse { Content = [REDACTED] }";
}
