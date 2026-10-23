using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogImportService(IFormCatalogRepository repository, IFormCatalogReader reader,
    IFormSpreadsheetReader spreadsheets, IUnitOfWork unitOfWork, IRequestValidator validator,
    FormCatalogAccessGuard access, FormCatalogItemRecorder recorder, TimeProvider timeProvider) : IFormCatalogImportService
{
    public async Task<Result<FormCatalogImportPreviewResponse>> PreviewAsync(Guid id, FormCatalogImportRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(true, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var current = await FindCatalogAsync(id, request.ExpectedRevision, cancellationToken);
        if (current.IsFailure) return current.Error;
        var sheet = await spreadsheets.ReadAsync(request.Content, request.FileName, cancellationToken);
        if (sheet.IsFailure)
        {
            var finalAccess = await access.CheckAsync(true, cancellationToken);
            return finalAccess.IsFailure ? finalAccess.Error : sheet.Error;
        }
        var items = await reader.ListAllItemsAsync(id, cancellationToken);
        var source = await WithFieldsAsync(new FormCatalogImportSource(id, current.Value.Revision,
            Enum.Parse<BusinessFormCatalogValueType>(current.Value.ValueType), items,
            current.Value.LastImportSha256, current.Value.LastImportAtUtc), current.Value.FieldsJson, cancellationToken);
        var plan = source.IsFailure ? Result.Failure<FormCatalogImportPlan>(source.Error)
            : FormCatalogImportPolicy.Build(source.Value, sheet.Value, Mode(request.Mode), Culture());
        var latest = await FindCatalogAsync(id, request.ExpectedRevision, cancellationToken);
        if (latest.IsFailure) return latest.Error;
        return plan.IsSuccess ? plan.Value.Preview : plan.Error;
    }

    public async Task<Result<FormCatalogSaveResponse>> ApplyAsync(Guid id, FormCatalogImportRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(true, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (request.SheetSha256 is null) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "sheetSha256");
        var current = await FindCatalogAsync(id, request.ExpectedRevision, cancellationToken);
        if (current.IsFailure) return current.Error;
        var sheet = await spreadsheets.ReadAsync(request.Content, request.FileName, cancellationToken);
        if (sheet.IsFailure) return sheet.Error;
        if (!string.Equals(request.SheetSha256, sheet.Value.Sha256, StringComparison.Ordinal)) return FormCatalogImportErrors.SheetChanged;
        var result = await unitOfWork.ExecuteInTransactionAsync(ct => ApplyCoreAsync(id, request, sheet.Value, ct),
            CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsFailure) return result.Error;
        int? usageCount = await access.HasPermissionAsync(actor.Value, BusinessFormsPermissions.Read, cancellationToken)
            ? await reader.CountUsageAsync(id, cancellationToken) : null;
        return result.Value with { Catalog = result.Value.Catalog with { UsageCount = usageCount } };
    }

    private async Task<Result<FormCatalogSaveResponse>> ApplyCoreAsync(Guid id, FormCatalogImportRequest request,
        FormSpreadsheet sheet, CancellationToken cancellationToken)
    {
        await repository.LockAsync(id, cancellationToken);
        var actor = await access.CheckAsync(true, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var catalog = await repository.GetAsync(id, cancellationToken);
        if (catalog is null) return FormCatalogErrors.NotFound;
        if (catalog.Kind != BusinessFormCatalogKind.Manual) return FormCatalogErrors.ManualItemsUnavailable;
        if (catalog.Revision != request.ExpectedRevision) return FormCatalogErrors.RevisionConflict;
        var items = await repository.ListItemsAsync(id, cancellationToken);
        var source = await WithFieldsAsync(new FormCatalogImportSource(id, catalog.Revision, catalog.ValueType,
            FormCatalogFieldPresentation.ToRows(items), catalog.LastImportSha256, catalog.LastImportAtUtc), catalog.FieldsJson, cancellationToken);
        if (source.IsFailure) return source.Error;
        var finalAccess = await access.CheckAsync(true, cancellationToken);
        if (finalAccess.IsFailure) return finalAccess.Error;
        var plan = FormCatalogImportPolicy.Build(source.Value, sheet, Mode(request.Mode), Culture());
        if (plan.IsFailure) return plan.Error;
        var confirmations = FormCatalogImportPolicy.ValidateConfirmations(plan.Value, request);
        if (confirmations.IsFailure) return confirmations.Error;
        var context = new FormCatalogFieldContext(source.Value.Fields ?? [], source.Value.Sources
            ?? new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>(), new HashSet<Guid>(), new HashSet<Guid>());
        var recorded = recorder.Record(catalog, items, plan.Value.Items, fieldContext: context);
        if (recorded.IsFailure) return recorded.Error;
        if (plan.Value.Items.Count > 0)
        {
            var marked = catalog.MarkImported(request.ExpectedRevision, sheet.Sha256, timeProvider.GetUtcNow().UtcDateTime);
            if (marked.IsFailure) return marked.Error;
        }
        var detail = new FormCatalogDetailResponse(catalog.Id, catalog.Key, ReadTexts(catalog.NameJson), ReadTexts(catalog.DescriptionJson),
            catalog.Kind.ToString(), catalog.Revision, catalog.IsActive, recorded.Value.Items.Count, null, null,
            ValueType: catalog.ValueType.ToString());
        detail = await FormCatalogFieldPresentation.DescribeAsync(detail, catalog.FieldsJson, catalog.PresentationJson,
            FormCatalogFieldPresentation.ToRows(recorded.Value.Items), reader, cancellationToken, source.Value.Sources);
        return new FormCatalogSaveResponse(detail, new Dictionary<string, Guid>(StringComparer.Ordinal));
    }

    private async Task<Result<FormCatalogImportSource>> WithFieldsAsync(FormCatalogImportSource source, string? fieldsJson,
        CancellationToken cancellationToken)
    {
        var fields = FormCatalogFieldPolicy.ReadFields(fieldsJson);
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>();
        foreach (var field in fields.Where(field => field.IsActive && field.Type == "catalog"))
        {
            if (field.SourceCatalogId is not { } sourceId || sourceId == source.Id) return FormCatalogErrors.FieldInvalid;
            if (sources.ContainsKey(sourceId)) continue;
            var catalog = await reader.FindAsync(sourceId, cancellationToken);
            if (catalog is null || !catalog.IsActive || catalog.Kind != "Manual") return FormCatalogErrors.FieldInvalid;
            sources.Add(sourceId, await reader.ListAllItemsAsync(sourceId, cancellationToken));
        }
        return source with { Fields = fields, Sources = sources };
    }

    private async Task<Result<FormCatalogStoredRow>> FindCatalogAsync(Guid id, long expectedRevision, CancellationToken cancellationToken)
    {
        var row = await reader.FindAsync(id, cancellationToken);
        var finalAccess = await access.CheckAsync(true, cancellationToken);
        if (finalAccess.IsFailure) return finalAccess.Error;
        if (row is null) return FormCatalogErrors.NotFound;
        if (row.Kind != "Manual") return FormCatalogErrors.ManualItemsUnavailable;
        if (row.Revision != expectedRevision) return FormCatalogErrors.RevisionConflict;
        return row;
    }

    private static FormCatalogImportMode Mode(string value) => value == "Replace" ? FormCatalogImportMode.Replace : FormCatalogImportMode.Append;
    private static string Culture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog text is invalid.");
}
