using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class BusinessFormRecordReference : Entity
{
    private BusinessFormRecordReference() { }
    public Guid RecordId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid FieldId { get; private set; }
    public Guid CatalogId { get; private set; }
    public long CatalogRevision { get; private set; }
    public Guid ItemId { get; private set; }
    public string Text { get; private set; } = "";
    public string? Detail { get; private set; }
    public string CatalogName { get; private set; } = "";
    public string? TranslationsJson { get; private set; }
    public string? Value { get; private set; }
    public string? ValueType { get; private set; }
    public string? FieldValuesJson { get; private set; }
    public static BusinessFormRecordReference Create(Guid recordId, Guid definitionId, Guid fieldId,
        Guid catalogId, long revision, Guid itemId, string text, string? detail, string catalogName, string? translationsJson = null,
        string? value = null, string? valueType = null, string? fieldValuesJson = null)
    {
        foreach (var id in new[] { recordId, definitionId, fieldId, catalogId, itemId }) ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!StorableText.IsStorable(text.AsSpan()) || detail is not null && !StorableText.IsStorable(detail.AsSpan()))
            throw new ArgumentException("Reference text must be storable.");
        return new() { RecordId = recordId, DefinitionId = definitionId, FieldId = fieldId, CatalogId = catalogId,
            CatalogRevision = revision, ItemId = itemId, Text = text, Detail = detail, CatalogName = catalogName, TranslationsJson = translationsJson,
            Value = value, ValueType = valueType, FieldValuesJson = fieldValuesJson };
    }
    public override string ToString() => "BusinessFormRecordReference { Content = [REDACTED] }";
}
