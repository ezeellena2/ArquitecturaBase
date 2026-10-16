using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public enum FormTaskState { Open, Approved, Rejected }
public sealed class BusinessFormTask : Entity, IAuditable
{
    private BusinessFormTask() { }
    public Guid DefinitionId { get; private set; }
    public Guid RecordId { get; private set; }
    public Guid RuleSetId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public long Revision { get; private set; }=1;
    public FormTaskState State { get; private set; }=FormTaskState.Open;
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormTask Open(Guid definitionId,Guid recordId,Guid ruleSetId,Guid runId,Guid roleId,Guid authorUserId,DateTime nowUtc)=>
        new(){DefinitionId=definitionId,RecordId=recordId,RuleSetId=ruleSetId,RunId=runId,RoleId=roleId,AuthorUserId=authorUserId,RequestedAtUtc=nowUtc};
}
