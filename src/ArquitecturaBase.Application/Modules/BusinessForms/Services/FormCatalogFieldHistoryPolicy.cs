using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogFieldHistoryPolicy
{
    /// <summary>
    /// Writes, when a response is captured, the union of the catalog fields its references sealed (never when reading): a row
    /// per form, catalog and field, that a later response replaces. Under the lock of definitions the capture holds.
    /// </summary>
    public static async Task RecordAsync(IFormRecordRepository records, Guid definitionId, long recordNumber,
        IEnumerable<FormRecordReferenceRow> references, CancellationToken cancellationToken)
    {
        var sealedFields = new Dictionary<(Guid CatalogId, Guid FieldId), (FormCatalogCapturedField Field, int Position)>();
        foreach (var reference in references)
        {
            if (reference.FieldValuesJson is not { } json) continue;
            var fields = FormDraftPolicy.Read<FormCatalogCapturedField[]>(json);
            for (var position = 0; position < fields.Length; position++)
                sealedFields[(reference.CatalogId, fields[position].Id)] = (fields[position], position);
        }
        if (sealedFields.Count == 0) return;
        var stored = (await records.ListCatalogFieldHistoryAsync(definitionId, sealedFields.Keys.Select(key => key.CatalogId).Distinct().ToArray(), cancellationToken))
            .ToDictionary(row => (row.CatalogId, row.FieldId));
        foreach (var (key, (field, position)) in sealedFields)
        {
            var name = FormDraftPolicy.Serialize(field.Name);
            if (stored.TryGetValue(key, out var existing)) existing.Seal(field.Key, name, field.Type, field.Role, position, recordNumber);
            else records.Add(BusinessFormRecordCatalogField.Create(definitionId, key.CatalogId, key.FieldId, field.Key, name, field.Type, field.Role, position, recordNumber));
        }
    }
}
