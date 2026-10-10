using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public enum FormPublicationState { Accepted, Preparing, Publishing, Uncertain, ActivationPending, Published, Failed }

public sealed class BusinessFormPublicationOperation : Entity, IAuditable
{
    private BusinessFormPublicationOperation() { }
    public Guid DefinitionId { get; private set; }
    public Guid VersionId { get; private set; }
    public Guid OperatorUserId { get; private set; }
    public Guid RequestId { get; private set; }
    public long DraftRevision { get; private set; }
    public long VersionNumber { get; private set; }
    public Guid? BaseActiveVersionId { get; private set; }
    public byte[] ProtectedManifest { get; private set; } = [];
    public FormPublicationState State { get; private set; }
    public string Stage { get; private set; } = "Accepted";
    public long Fence { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public DateTime? NextCheckAtUtc { get; private set; }
    public DateTime AcceptedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public bool IsOpen => State is not (FormPublicationState.Published or FormPublicationState.Failed);

    public static BusinessFormPublicationOperation Accept(Guid definitionId, Guid versionId, Guid actor, Guid requestId,
        long draftRevision, long versionNumber, Guid? baseActiveVersionId, byte[] protectedManifest, DateTime nowUtc) => new()
    {
        DefinitionId = definitionId, VersionId = versionId, OperatorUserId = actor, RequestId = requestId,
        DraftRevision = draftRevision, VersionNumber = versionNumber, BaseActiveVersionId = baseActiveVersionId,
        ProtectedManifest = protectedManifest.ToArray(), AcceptedAtUtc = nowUtc, UpdatedAtUtc = nowUtc,
        State = FormPublicationState.Accepted,
    };

    public void SealManifest(byte[] protectedManifest)
    {
        if (ProtectedManifest.Length != 0) throw new InvalidOperationException("A publication manifest is immutable.");
        ProtectedManifest = protectedManifest.ToArray();
    }
    public Result<long> Reserve(DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (leaseDuration <= TimeSpan.Zero || !IsOpen || LeaseExpiresAtUtc > nowUtc || NextCheckAtUtc > nowUtc || Fence == long.MaxValue)
            return FormPublicationErrors.StateConflict;
        Fence++;
        LeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        if (State != FormPublicationState.ActivationPending) State = FormPublicationState.Preparing;
        UpdatedAtUtc = nowUtc;
        return Fence;
    }

    public Result RecordChannelResult(long fence, DateTime nowUtc, string state, string stage)
    {
        if (fence != Fence || !IsOpen || LeaseExpiresAtUtc is null || LeaseExpiresAtUtc <= nowUtc)
            return FormPublicationErrors.StateConflict;
        var mapped = state switch
        {
            "Published" => FormPublicationState.ActivationPending,
            "Failed" => FormPublicationState.Failed,
            "Uncertain" => FormPublicationState.Uncertain,
            "Publishing" or "Accepted" or "Preparing" => FormPublicationState.Publishing,
            _ => (FormPublicationState?)null,
        };
        if (mapped is null) return FormPublicationErrors.StateConflict;
        State = mapped.Value; Stage = stage; UpdatedAtUtc = nowUtc; LeaseExpiresAtUtc = null;
        NextCheckAtUtc = IsOpen && State != FormPublicationState.ActivationPending ? nowUtc.AddSeconds(30) : null;
        return Result.Success();
    }

    public Result DeferActivation(long fence, DateTime nowUtc)
    {
        if (fence != Fence || State != FormPublicationState.ActivationPending) return FormPublicationErrors.StateConflict;
        State = FormPublicationState.Uncertain; Stage = "ActivationConflict"; UpdatedAtUtc = nowUtc;
        NextCheckAtUtc = nowUtc.AddMinutes(5); return Result.Success();
    }
    public Result Complete(long fence, DateTime nowUtc)
    {
        if (fence != Fence || State != FormPublicationState.ActivationPending) return FormPublicationErrors.StateConflict;
        State = FormPublicationState.Published; Stage = "Complete"; UpdatedAtUtc = nowUtc;
        LeaseExpiresAtUtc = null; NextCheckAtUtc = null;
        return Result.Success();
    }
    public override string ToString() => $"BusinessFormPublicationOperation {{ Id = {Id}, State = {State}, Content = [REDACTED] }}";
}
