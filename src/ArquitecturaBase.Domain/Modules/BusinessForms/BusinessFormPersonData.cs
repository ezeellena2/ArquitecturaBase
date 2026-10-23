using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public sealed class BusinessFormPersonData : Entity, IAuditable
{
    private BusinessFormPersonData() { }
    public Guid UserId { get; private set; }
    public string ValuesJson { get; private set; } = "{}";
    public long Revision { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormPersonData Create(Guid userId, string valuesJson) => new() { UserId = userId, ValuesJson = valuesJson };
    public Result Update(long expectedRevision, string valuesJson)
    {
        if (Revision != expectedRevision || Revision >= 9007199254740991) return FormPersonDataErrors.RevisionConflict;
        ValuesJson = valuesJson; Revision++; return Result.Success();
    }
}
