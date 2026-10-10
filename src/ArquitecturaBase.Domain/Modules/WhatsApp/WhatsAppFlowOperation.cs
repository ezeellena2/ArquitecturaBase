using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed class WhatsAppFlowOperation : Entity, IAuditable
{
    private WhatsAppFlowOperation() { }
    public Guid NeutralOperationId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid VersionId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public byte[] ProtectedManifest { get; private set; } = [];
    public long Fence { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public DateTime? NextCheckAtUtc { get; private set; }
    public bool Completed { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static WhatsAppFlowOperation Accept(Guid neutralOperationId, Guid definitionId, Guid versionId, Guid actor) => new()
        { NeutralOperationId = neutralOperationId, DefinitionId = definitionId, VersionId = versionId, ActorUserId = actor };
    public void SealManifest(byte[] protectedManifest)
    {
        if (ProtectedManifest.Length != 0) throw new InvalidOperationException("The Flow manifest is immutable.");
        ProtectedManifest = protectedManifest.ToArray();
    }
    public Result<long> Reserve(DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (Completed || LeaseExpiresAtUtc > nowUtc || NextCheckAtUtc > nowUtc || Fence == long.MaxValue || leaseDuration <= TimeSpan.Zero)
            return FlowErrors.StateConflict;
        Fence++; LeaseExpiresAtUtc = nowUtc.Add(leaseDuration); return Fence;
    }
    public bool OwnsLease(long fence, DateTime nowUtc) => Fence == fence && LeaseExpiresAtUtc > nowUtc && !Completed;
    public void Release(DateTime nowUtc, bool completed, bool requiresReview)
    {
        LeaseExpiresAtUtc = null; Completed = completed;
        NextCheckAtUtc = completed ? null : nowUtc.AddSeconds(requiresReview ? 300 : 15);
    }
    public override string ToString() => $"WhatsAppFlowOperation {{ Id = {Id}, Content = [REDACTED] }}";
}
