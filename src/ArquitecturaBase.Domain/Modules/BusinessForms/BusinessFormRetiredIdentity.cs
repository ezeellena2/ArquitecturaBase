using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public enum BusinessFormIdentityKind
{
    Field,
    Choice,
}

public sealed class BusinessFormRetiredIdentity : Entity, IAuditable
{
    private BusinessFormRetiredIdentity() { }

    public Guid DefinitionId { get; private set; }
    public Guid IdentityId { get; private set; }
    public BusinessFormIdentityKind Kind { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static BusinessFormRetiredIdentity Create(Guid definitionId, Guid identityId, BusinessFormIdentityKind kind)
    {
        if (definitionId == Guid.Empty || identityId == Guid.Empty)
            throw new ArgumentException("A retired identity requires non-empty identifiers.");

        return new BusinessFormRetiredIdentity { DefinitionId = definitionId, IdentityId = identityId, Kind = kind };
    }

    public override string ToString() => $"BusinessFormRetiredIdentity {{ Id = {Id}, DefinitionId = {DefinitionId}, Kind = {Kind}, IdentityId = [REDACTED] }}";
}
