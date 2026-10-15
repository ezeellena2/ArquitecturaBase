using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class BusinessFormRecordFile : Entity
{
    private BusinessFormRecordFile() { }
    public Guid DefinitionId { get; private set; }
    public Guid RecordId { get; private set; }
    public Guid FieldId { get; private set; }
    public Guid FileId { get; private set; }
    public int Position { get; private set; }
    public static BusinessFormRecordFile Create(Guid definitionId, Guid recordId, Guid fieldId, Guid fileId, int position) =>
        new() { DefinitionId = definitionId, RecordId = recordId, FieldId = fieldId, FileId = fileId, Position = position };
}
