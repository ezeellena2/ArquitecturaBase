using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The party an issuer completed when it queued a response (a customer written by its tax id) joins the catalog its standard
/// brings, in the transaction that keeps the outcome of the run: an active option with its code, its name and the values its
/// fields know (a catalog field, the active option with the code it gave), under the locks of the catalog and its sources and
/// advancing its revision. A missing catalog or one that is not Manual, a party it already has by code or tax id and an option
/// that does not fit (a required field the party does not bring, a repeated unique value, the 10,000) leave everything as it was:
/// the issuance goes on. It never saves on its own.
/// </summary>
internal sealed class FormIssuedCatalogItemRecorder(IFormLibraryReader library, IFormCatalogReader reader, IFormCatalogRepository repository,
    IFormCatalogItemCodeReader codes, FormCatalogItemRecorder recorder)
{
    private const int MaxCodeLength = 80;

    public async Task AddAsync(StructuredIssueCatalogItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var code = item.Code.Trim();
        if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength || string.IsNullOrWhiteSpace(item.Text)) return;
        if ((await library.ListCatalogsByKeyAsync([item.CatalogKey], cancellationToken)).GetValueOrDefault(item.CatalogKey) is not { } found
            || await reader.FindAsync(found.Id, cancellationToken) is not { Kind: "Manual" } row) return;
        // The fields read before the locks say which sources to lock, in order; one that changed its sources since then is left alone.
        var locked = Sources(row.FieldsJson);
        foreach (var catalogId in locked.Append(row.Id).Distinct().Order()) await repository.LockAsync(catalogId, cancellationToken);
        if (await repository.GetAsync(row.Id, cancellationToken) is not { Kind: BusinessFormCatalogKind.Manual } catalog
            || !Sources(catalog.FieldsJson).All(locked.Contains)) return;
        var fields = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
        var items = await repository.ListItemsAsync(catalog.Id, cancellationToken);
        if (Known(items, fields, item, code)) return;
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>();
        foreach (var sourceId in Sources(catalog.FieldsJson))
        {
            if (await reader.FindAsync(sourceId, cancellationToken) is not { IsActive: true } source || !FormCatalogKindPolicy.HasItems(source.Kind)) return;
            sources[sourceId] = await reader.ListAllItemsAsync(sourceId, cancellationToken);
        }
        // A value without its field, one that does not fit it or a code its source does not have is dropped, as «Completar» does.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in fields.Where(field => field.IsActive))
        {
            if (item.Fields.GetValueOrDefault(field.Key) is not { } value) continue;
            if (field.Type != "catalog")
            {
                if (FormCatalogFieldPolicy.Normalize(field with { Required = false }, value).IsSuccess) values[field.Id.ToString("D")] = value;
            }
            else if (await codes.FindActiveAsync(field.SourceCatalogId!.Value, value, cancellationToken) is { } optionId)
                values[field.Id.ToString("D")] = optionId.ToString("D");
        }
        var (text, detail) = Texts(item.Text.Trim());
        var option = new FormCatalogManualItemSaveRequest("issued", null, code, new Dictionary<string, string?> { [catalog.OriginalCulture] = text },
            detail is null ? null : new Dictionary<string, string?> { [catalog.OriginalCulture] = detail }, true, Fields: values);
        var context = new FormCatalogFieldContext(fields, sources, new HashSet<Guid>(), new HashSet<Guid>(), fields.ToDictionary(field => field.Id),
            catalog.OriginalCulture);
        if (recorder.Record(catalog, items, [option], fieldContext: context).IsFailure) return;
        // A new option changes the catalog: whoever saves it with the revision they read before gets the 409.
        var advanced = catalog.UpdateManual(catalog.Revision, Read(catalog.NameJson), Read(catalog.DescriptionJson));
        if (advanced.IsFailure) throw new InvalidOperationException("The catalog of an issued party could not advance its revision: " + advanced.Error.Code);
    }

    private static HashSet<Guid> Sources(string? fieldsJson) => FormCatalogFieldPolicy.ReadFields(fieldsJson)
        .Where(field => field.IsActive && field.SourceCatalogId is not null).Select(field => field.SourceCatalogId!.Value).ToHashSet();

    // The same party: an option with its code, or with its tax id in the field that holds it, compared by its letters and digits.
    private static bool Known(IReadOnlyList<BusinessFormCatalogItem> items, IReadOnlyList<BusinessFormCatalogField> fields, StructuredIssueCatalogItem item,
        string code)
    {
        var taxId = Identity(item.Fields.GetValueOrDefault(item.TaxIdField) ?? code);
        var key = fields.FirstOrDefault(field => field.Key == item.TaxIdField)?.Id.ToString("D");
        return items.Any(stored => string.Equals(stored.Code, code, StringComparison.OrdinalIgnoreCase)
            || key is not null && taxId.Length > 0 && FormCatalogFieldPolicy.ReadValues(stored.FieldValuesJson).TryGetValue(key, out var value)
                && Identity(FormCatalogFieldPolicy.Input(value)) == taxId);
    }

    private static string Identity(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    // The text of an option fits in 30 characters: a longer name keeps its first 30 as the text and goes whole in the detail.
    private static (string Text, string? Detail) Texts(string name) => name.Length <= BusinessFormCatalogItem.MaxTextLength ? (name, null)
        : (name[..BusinessFormCatalogItem.MaxTextLength].TrimEnd(), name.Length <= BusinessFormCatalogItem.MaxDetailLength ? name : name[..BusinessFormCatalogItem.MaxDetailLength]);

    private static Dictionary<string, string> Read(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog text is invalid.");
}
