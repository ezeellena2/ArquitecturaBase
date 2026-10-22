using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppFlowStoredManifest(int Format, string RequestFingerprint, string PublicationFingerprint,
    WhatsAppFlowAccountContext Account, IReadOnlyList<string> RequiredPermissions)
{
    public override string ToString() => "WhatsAppFlowStoredManifest { Content = [REDACTED] }";
}
public sealed record WhatsAppFlowProjection(int Format, string Json, string Hash, string ProfileRevision, int CodecVersion,
    string Culture, IReadOnlyList<WhatsAppFlowProjectionField> Mapping)
{
    public override string ToString() => "WhatsAppFlowProjection { Content = [REDACTED] }";
}
public sealed record WhatsAppFlowProjectionField(Guid FieldId, string Key, string Type, bool Required, int? MaxLength, IReadOnlyList<Guid> Choices,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ArquitecturaBase.Application.Models.StructuredData.StructuredFormFileSettings? Files = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ArquitecturaBase.Application.Models.StructuredData.StructuredFormSelectionSettings? Selection = null);
public sealed record WhatsAppFlowDiagnostic(Guid? FieldId, Guid? ChoiceId, string Path, string Culture);
public sealed record WhatsAppFlowOperationRow(Guid Id, Guid NeutralOperationId, Guid DefinitionId, Guid VersionId, Guid ActorUserId,
    byte[] ProtectedManifest, DateTime? NextCheckAtUtc, IReadOnlyList<WhatsAppFlowBindingRow> Bindings)
{
    public override string ToString() => "WhatsAppFlowOperationRow { Content = [REDACTED] }";
}
public sealed record WhatsAppFlowBindingRow(Guid Id, string Culture, string TechnicalName, byte[] ProtectedProjection,
    string? MetaFlowId, string State, string Step, string? FailureCode, string DiagnosticsJson)
{
    public override string ToString() => "WhatsAppFlowBindingRow { Content = [REDACTED] }";
}
public sealed record WhatsAppFlowWorkResponse(Guid OperationId, Guid NeutralOperationId, Guid BindingId, long Fence, WhatsAppFlowStep Step, string? MetaFlowId,
    string TechnicalName, WhatsAppFlowStoredManifest Manifest, WhatsAppFlowProjection Projection, DateTime LeaseExpiresAtUtc)
{
    public override string ToString() => "WhatsAppFlowWorkResponse { Content = [REDACTED] }";
}
public sealed record WhatsAppFlowValidationPointer(int? FieldIndex, int? ChoiceIndex, string Property, int ScreenIndex = 0);
public sealed record WhatsAppFlowEffectResponse(bool Success, bool Uncertain, string? MetaFlowId, IReadOnlyList<WhatsAppFlowValidationPointer> ValidationErrors, string? FailureCode = null)
{
    public override string ToString() => "WhatsAppFlowEffectResponse { Content = [REDACTED] }";
}
public sealed record WhatsAppFlowVerificationResponse(bool Published, bool Draft, bool AssetAndOwnerMatch, bool Validated, bool Healthy, int? CreationCandidateCount = null);
