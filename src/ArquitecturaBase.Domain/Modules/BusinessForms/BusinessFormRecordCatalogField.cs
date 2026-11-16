using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>
/// The union of the catalog fields that the responses of a form have sealed, one row per form, catalog and field. The capture writes it
/// (never a read), so the table never has to open the sealed fields of every response to know which subcolumns to show. The row
/// keeps the latest definition of the field sealed: the one of the response with the highest number.
/// </summary>
public sealed class BusinessFormRecordCatalogField : Entity
{
    private BusinessFormRecordCatalogField() { }
    public Guid DefinitionId { get; private set; }
    public Guid CatalogId { get; private set; }
    public Guid FieldId { get; private set; }
    public string Key { get; private set; } = "";
    public string NameJson { get; private set; } = "{}";
    public string Type { get; private set; } = "text";
    public string Role { get; private set; } = "data";
    public int Position { get; private set; }
    public long RecordNumber { get; private set; }

    public static BusinessFormRecordCatalogField Create(Guid definitionId, Guid catalogId, Guid fieldId, string key, string nameJson,
        string type, string role, int position, long recordNumber)
    {
        foreach (var id in new[] { definitionId, catalogId, fieldId }) ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new() { DefinitionId = definitionId, CatalogId = catalogId, FieldId = fieldId, Key = key, NameJson = nameJson, Type = type,
            Role = role, Position = position, RecordNumber = recordNumber };
    }

    /// <summary>A later response (or a correction, which keeps its number) replaces the definition; an earlier one never does.</summary>
    public void Seal(string key, string nameJson, string type, string role, int position, long recordNumber)
    {
        if (recordNumber < RecordNumber) return;
        Key = key; NameJson = nameJson; Type = type; Role = role; Position = position; RecordNumber = recordNumber;
    }

    public override string ToString() => "BusinessFormRecordCatalogField { Content = [REDACTED] }";
}
