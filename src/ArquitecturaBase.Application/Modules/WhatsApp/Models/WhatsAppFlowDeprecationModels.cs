namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public enum WhatsAppFlowDeprecationStep { Deprecate, Verify }
public sealed record WhatsAppFlowDeprecationCandidateRow(Guid BindingId, Guid OperationId, Guid NeutralOperationId,
    Guid DefinitionId, Guid VersionId);
public sealed record WhatsAppFlowDeprecationWorkResponse(Guid OperationId, Guid NeutralOperationId, Guid BindingId,
    Guid DefinitionId, Guid VersionId, long Fence, WhatsAppFlowDeprecationStep Step, string MetaFlowId,
    string TechnicalName, WhatsAppFlowStoredManifest Manifest, DateTime LeaseExpiresAtUtc)
{
    public override string ToString() => "WhatsAppFlowDeprecationWorkResponse { Content = [REDACTED] }";
}
