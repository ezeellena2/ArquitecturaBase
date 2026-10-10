using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public sealed class BusinessFormDraftChange : Entity, IAuditable
{
    private BusinessFormDraftChange() { }
    public Guid OperatorUserId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public string Kind { get; private set; } = "";
    public long? ExpectedRevision { get; private set; }
    public long ConfirmedRevision { get; private set; }
    public int ResultFormat { get; private set; } = 1;
    public byte[]? ProtectedResult { get; private set; }
    public DateTime ResultExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormDraftChange Create(Guid actor, Guid requestId, Guid definitionId, string kind,
        long? expectedRevision, long confirmedRevision, DateTime expiresAtUtc) => new()
        { OperatorUserId = actor, RequestId = requestId, DefinitionId = definitionId, Kind = kind,
            ExpectedRevision = expectedRevision, ConfirmedRevision = confirmedRevision, ResultExpiresAtUtc = expiresAtUtc };
    public void SetProtectedResult(byte[] result) => ProtectedResult = (byte[])result.Clone();
    public void ExpireResult(DateTime nowUtc) { if (nowUtc >= ResultExpiresAtUtc) ProtectedResult = null; }
    public override string ToString() => $"BusinessFormDraftChange {{ Id = {Id}, Content = [REDACTED] }}";
}
