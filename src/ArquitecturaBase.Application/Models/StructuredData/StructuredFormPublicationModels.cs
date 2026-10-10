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
public sealed record StructuredFormPreviewQuery(Guid OperationId, Guid DefinitionId, Guid VersionId, string PublicationFingerprint, string Culture)
{
    public override string ToString() => "StructuredFormPreviewQuery { Content = [REDACTED] }";
}
public sealed record StructuredFormPreviewResponse(string Url, DateTime ExpiresAtUtc)
{
    public override string ToString() => "StructuredFormPreviewResponse { Content = [REDACTED] }";
}
