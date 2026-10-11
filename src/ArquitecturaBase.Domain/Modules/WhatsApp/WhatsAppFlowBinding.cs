using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public enum WhatsAppFlowPublicationState { Accepted, Publishing, Uncertain, Published, Failed }
public enum WhatsAppFlowStep { Create, Upload, Validate, Publish, Verify }

public sealed class WhatsAppFlowBinding : Entity, IAuditable
{
    private WhatsAppFlowBinding() { }
    public Guid OperationId { get; private set; }
    public Guid VersionId { get; private set; }
    public string Culture { get; private set; } = "";
    public string TechnicalName { get; private set; } = "";
    public byte[] ProtectedProjection { get; private set; } = [];
    public string? MetaFlowId { get; private set; }
    public WhatsAppFlowPublicationState State { get; private set; }
    public WhatsAppFlowStep Step { get; private set; }
    public bool InFlight { get; private set; }
    public long Fence { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string DiagnosticsJson { get; private set; } = "[]";
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static WhatsAppFlowBinding Create(Guid operationId, Guid versionId, string culture, string technicalName, byte[] protectedProjection) => new()
    {
        OperationId = operationId, VersionId = versionId, Culture = culture, TechnicalName = technicalName,
        ProtectedProjection = protectedProjection.ToArray(), State = WhatsAppFlowPublicationState.Accepted, Step = WhatsAppFlowStep.Create,
    };
    public void SealProjection(byte[] protectedProjection)
    {
        if (ProtectedProjection.Length != 0) throw new InvalidOperationException("The Flow projection is immutable.");
        ProtectedProjection = protectedProjection.ToArray();
    }
    public Result<WhatsAppFlowStep> Begin(long fence, DateTime nowUtc)
    {
        if (State is WhatsAppFlowPublicationState.Published or WhatsAppFlowPublicationState.Failed || fence <= Fence) return FlowErrors.StateConflict;
        Fence = fence;
        if (InFlight || State == WhatsAppFlowPublicationState.Uncertain)
        {
            State = WhatsAppFlowPublicationState.Uncertain;
            return WhatsAppFlowStep.Verify;
        }
        State = WhatsAppFlowPublicationState.Publishing;
        InFlight = false;
        VerifiedAtUtc = null;
        return Step;
    }
    public Result MarkEffectStarted(long fence)
    {
        if (fence != Fence || State != WhatsAppFlowPublicationState.Publishing || InFlight
            || Step is not (WhatsAppFlowStep.Create or WhatsAppFlowStep.Upload or WhatsAppFlowStep.Publish)) return FlowErrors.StateConflict;
        InFlight = true;
        return Result.Success();
    }
    public Result RecordEffect(long fence, DateTime nowUtc, string? metaFlowId, bool success, bool hasValidationErrors, bool uncertain)
    {
        if (fence != Fence || !InFlight || State is WhatsAppFlowPublicationState.Published or WhatsAppFlowPublicationState.Failed) return FlowErrors.StateConflict;
        if (Step == WhatsAppFlowStep.Create && !string.IsNullOrWhiteSpace(metaFlowId)) MetaFlowId = metaFlowId;
        if (uncertain || (Step == WhatsAppFlowStep.Create && success && string.IsNullOrWhiteSpace(metaFlowId)))
        {
            State = WhatsAppFlowPublicationState.Uncertain; return Result.Success();
        }
        InFlight = false;
        if (!success || hasValidationErrors)
        {
            State = WhatsAppFlowPublicationState.Failed;
            FailureCode = hasValidationErrors ? FlowErrors.ValidationFailed.Code : FlowErrors.ProviderRejected.Code;
            return Result.Success();
        }
        switch (Step)
        {
            case WhatsAppFlowStep.Create: MetaFlowId = metaFlowId; Step = WhatsAppFlowStep.Upload; break;
            case WhatsAppFlowStep.Upload: Step = WhatsAppFlowStep.Validate; break;
            case WhatsAppFlowStep.Publish: Step = WhatsAppFlowStep.Verify; break;
            default: return FlowErrors.StateConflict;
        }
        return Result.Success();
    }
    public Result RetryWithoutEffect(long fence)
    {
        if (fence != Fence || State is WhatsAppFlowPublicationState.Published or WhatsAppFlowPublicationState.Failed)
            return FlowErrors.StateConflict;
        if (State == WhatsAppFlowPublicationState.Uncertain) return Result.Success();
        InFlight = false;
        State = Step switch
        {
            WhatsAppFlowStep.Create when MetaFlowId is null => WhatsAppFlowPublicationState.Accepted,
            WhatsAppFlowStep.Verify => WhatsAppFlowPublicationState.Uncertain,
            _ => WhatsAppFlowPublicationState.Publishing,
        };
        return Result.Success();
    }
    public Result PreserveUncertainWithoutEffect(long fence)
    {
        if (fence != Fence || State is WhatsAppFlowPublicationState.Published or WhatsAppFlowPublicationState.Failed)
            return FlowErrors.StateConflict;
        InFlight = false;
        State = WhatsAppFlowPublicationState.Uncertain;
        return Result.Success();
    }
    public Result RecordVerification(long fence, DateTime nowUtc, bool published, bool draft, bool assetAndOwnerMatch, bool validated, bool healthy)
    {
        if (fence != Fence || State is WhatsAppFlowPublicationState.Published or WhatsAppFlowPublicationState.Failed) return FlowErrors.StateConflict;
        if (MetaFlowId is null || !assetAndOwnerMatch || !validated || !healthy)
        {
            State = WhatsAppFlowPublicationState.Uncertain; InFlight = false; return Result.Success();
        }
        if (published)
        {
            State = WhatsAppFlowPublicationState.Published; Step = WhatsAppFlowStep.Verify;
            VerifiedAtUtc = nowUtc; InFlight = false; FailureCode = null; return Result.Success();
        }
        if (draft && Step is WhatsAppFlowStep.Upload or WhatsAppFlowStep.Validate)
        {
            State = WhatsAppFlowPublicationState.Publishing; Step = WhatsAppFlowStep.Publish; InFlight = false;
            return Result.Success();
        }
        State = WhatsAppFlowPublicationState.Uncertain; return Result.Success();
    }
    public void SuspendAuthorization()
    {
        if (State == WhatsAppFlowPublicationState.Published) return;
        if (MetaFlowId is not null || InFlight || State == WhatsAppFlowPublicationState.Uncertain)
        { State = WhatsAppFlowPublicationState.Uncertain; FailureCode = FlowErrors.AuthorizationChanged.Code; }
        else Reject(FlowErrors.AuthorizationChanged.Code, "[]");
    }
    public void Reject(string code, string diagnosticsJson)
    {
        if (State == WhatsAppFlowPublicationState.Published) throw new InvalidOperationException("A verified Flow result cannot be replaced.");
        State = WhatsAppFlowPublicationState.Failed; FailureCode = code; DiagnosticsJson = diagnosticsJson; InFlight = false;
    }
    public void SetDiagnostics(string diagnosticsJson) => DiagnosticsJson = diagnosticsJson;
    public override string ToString() => $"WhatsAppFlowBinding {{ Id = {Id}, State = {State}, Content = [REDACTED] }}";
}
