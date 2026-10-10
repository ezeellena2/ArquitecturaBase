using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class BusinessFormField : Entity
{
    private BusinessFormField() { }
    private BusinessFormField(Guid id) : base(id) { }
    public Guid DefinitionId { get; private set; }
    public string StableKey { get; private set; } = "";
    public string Type { get; private set; } = "";
    public static BusinessFormField Register(Guid fieldId, Guid definitionId, string stableKey, string type) => new(fieldId)
        { DefinitionId = definitionId, StableKey = stableKey, Type = type };
}
