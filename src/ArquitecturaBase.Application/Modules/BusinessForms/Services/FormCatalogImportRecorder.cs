using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The import of a sheet already read, an upload's or the assistant's copy's, inside the boundary its service opens: the catalog
// and the sources of its active fields locked in order, permissions and revision checked again, the plan rebuilt with its
// confirmations, the items recorded and the sheet marked as imported. It never opens a boundary of its own.
internal sealed class FormCatalogImportRecorder(IFormCatalogRepository repository, IFormCatalogReader reader, FormCatalogAccessGuard access,
    FormCatalogItemRecorder recorder, TimeProvider timeProvider)
{
    // The menu's culture, which reads the numbers and dates typed as text.
    public static string Culture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";

    // fieldsJson is the catalog's as it was read before the boundary: it says which sources to lock, and the revision checked
    // under the lock says it did not change.
    public async Task<Result<FormCatalogSaveResponse>> RecordAsync(Guid id, string? fieldsJson, FormCatalogImportApplication application,
        FormSpreadsheet sheet, CancellationToken cancellationToken)
    {
        var sourceIds = FormCatalogFieldPolicy.ReadFields(fieldsJson)
            .Where(field => field.IsActive && field.SourceCatalogId is not null).Select(field => field.SourceCatalogId!.Value);
        foreach (var catalogId in sourceIds.Append(id).Distinct().Order()) await repository.LockAsync(catalogId, cancellationToken);
        var actor = await access.CheckAsync(true, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var canReadForms = await access.HasPermissionAsync(actor.Value, BusinessFormsPermissions.Read, cancellationToken);
        var catalog = await repository.GetAsync(id, cancellationToken);
        if (catalog is null) return FormCatalogErrors.NotFound;
        if (catalog.Kind != BusinessFormCatalogKind.Manual) return FormCatalogErrors.ManualItemsUnavailable;
        if (catalog.Revision != application.ExpectedRevision) return FormCatalogErrors.RevisionConflict;
        var items = await repository.ListItemsAsync(id, cancellationToken);
        var source = await WithFieldsAsync(new FormCatalogImportSource(id, catalog.Revision, catalog.ValueType,
            catalog.OriginalCulture, FormCatalogFieldPresentation.ToRows(items), catalog.LastImportSha256, catalog.LastImportAtUtc), catalog.FieldsJson, cancellationToken);
        if (source.IsFailure) return source.Error;
        var finalAccess = await access.CheckAsync(true, cancellationToken);
        if (finalAccess.IsFailure) return finalAccess.Error;
        var plan = FormCatalogImportPolicy.Build(source.Value, sheet, application.Mode, Culture());
        if (plan.IsFailure) return plan.Error;
        var confirmations = FormCatalogImportPolicy.ValidateConfirmations(plan.Value, application);
        if (confirmations.IsFailure) return confirmations.Error;
        var fields = source.Value.Fields ?? [];
        var context = new FormCatalogFieldContext(fields, source.Value.Sources
            ?? new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>(), new HashSet<Guid>(), new HashSet<Guid>(),
            fields.ToDictionary(field => field.Id), Culture());
        var recorded = recorder.Record(catalog, items, plan.Value.Items, fieldContext: context);
        if (recorded.IsFailure) return recorded.Error;
        if (plan.Value.Items.Count > 0)
        {
            var marked = catalog.MarkImported(application.ExpectedRevision, sheet.Sha256, timeProvider.GetUtcNow().UtcDateTime);
            if (marked.IsFailure) return marked.Error;
        }
        var detail = new FormCatalogDetailResponse(catalog.Id, catalog.Key, ReadTexts(catalog.NameJson), ReadTexts(catalog.DescriptionJson),
            catalog.OriginalCulture, catalog.Kind.ToString(), catalog.Revision, catalog.IsActive, recorded.Value.Items.Count, null, null,
            ValueType: catalog.ValueType.ToString());
        detail = await FormCatalogFieldPresentation.DescribeAsync(detail, catalog.FieldsJson, catalog.PresentationJson,
            FormCatalogFieldPresentation.ToRows(recorded.Value.Items), reader, canReadForms, cancellationToken, source.Value.Sources);
        return new FormCatalogSaveResponse(detail, new Dictionary<string, Guid>(StringComparer.Ordinal),
            new Dictionary<string, Guid>(StringComparer.Ordinal));
    }

    // The active fields and the options of the Manual catalogs they take their values from.
    public async Task<Result<FormCatalogImportSource>> WithFieldsAsync(FormCatalogImportSource source, string? fieldsJson,
        CancellationToken cancellationToken)
    {
        var fields = FormCatalogFieldPolicy.ReadFields(fieldsJson);
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>();
        foreach (var field in fields.Where(field => field.IsActive && field.Type == "catalog"))
        {
            if (field.SourceCatalogId is not { } sourceId || sourceId == source.Id) return FormCatalogErrors.FieldInvalid;
            if (sources.ContainsKey(sourceId)) continue;
            var catalog = await reader.FindAsync(sourceId, cancellationToken);
            if (catalog is null || !catalog.IsActive || !FormCatalogKindPolicy.HasItems(catalog.Kind)) return FormCatalogErrors.FieldInvalid;
            sources.Add(sourceId, await reader.ListAllItemsAsync(sourceId, cancellationToken));
        }
        return source with { Fields = fields, Sources = sources };
    }

    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog text is invalid.");
}
