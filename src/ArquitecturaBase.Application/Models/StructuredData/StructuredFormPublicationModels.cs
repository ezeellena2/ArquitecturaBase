namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormPublicationCulture(string Culture, string CompilationFingerprint);
public sealed record StructuredFormPublicationRequest(Guid OperationId, Guid DefinitionId, Guid VersionId, Guid ActorUserId,
    IReadOnlyList<string> RequiredPermissions, string PublicationFingerprint, string ProfileRevision,
    IReadOnlyList<StructuredFormPublicationCulture> Cultures, StructuredFormCheckRequest Snapshot)
{
    public override string ToString() => "StructuredFormPublicationRequest { Content = [REDACTED] }";
}
public sealed record StructuredFormPublicationQuery(Guid OperationId, Guid DefinitionId, Guid VersionId, string PublicationFingerprint)
{
    public override string ToString() => "StructuredFormPublicationQuery { Content = [REDACTED] }";
}
public sealed record StructuredFormPublicationResponse(Guid OperationId, Guid DefinitionId, Guid VersionId, string PublicationFingerprint,
    string State, string Stage, IReadOnlyList<StructuredFormIssueResponse> Issues, DateTime? NextCheckAtUtc, bool RequiresReview)
{
    public override string ToString() => "StructuredFormPublicationResponse { Content = [REDACTED] }";
}
/// <summary>
/// The official preview of a published version in a culture. <paramref name="Catalogs"/> are sample options for its catalog
/// questions (VO1-A, 2026-11-06), so a channel that can show the preview interactively lets it be gone through.
/// </summary>
public sealed record StructuredFormPreviewQuery(Guid OperationId, Guid DefinitionId, Guid VersionId, string PublicationFingerprint, string Culture,
    IReadOnlyList<StructuredFormCatalogOffer>? Catalogs = null)
{
    public override string ToString() => "StructuredFormPreviewQuery { Content = [REDACTED] }";
}
public sealed record StructuredFormPreviewResponse(string Url, DateTime ExpiresAtUtc)
{
    public override string ToString() => "StructuredFormPreviewResponse { Content = [REDACTED] }";
}
