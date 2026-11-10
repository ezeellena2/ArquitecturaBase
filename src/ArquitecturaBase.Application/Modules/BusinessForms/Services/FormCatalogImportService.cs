using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The import of a sheet into a Manual catalog: an uploaded file, or (Stage 2 of the assistant authoring) the private copy of the
// spreadsheet a proposal mapped. Both go through the same review without a boundary and the same single boundary to apply.
internal sealed class FormCatalogImportService(IFormCatalogReader reader, IFormSpreadsheetReader spreadsheets, IUnitOfWork unitOfWork,
    IRequestValidator validator, FormCatalogAccessGuard access, FormCatalogImportRecorder imports, FormCatalogAssistantImportPolicy assistant)
    : IFormCatalogImportService
{
    public async Task<Result<FormCatalogImportPreviewResponse>> PreviewAsync(Guid id, FormCatalogImportRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(true, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        return await PreviewSheetAsync(id, request.ExpectedRevision, Mode(request.Mode),
            (_, ct) => spreadsheets.ReadAsync(request.Content, request.FileName, ct), cancellationToken);
    }

    public async Task<Result<FormCatalogSaveResponse>> ApplyAsync(Guid id, FormCatalogImportRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(true, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (request.SheetSha256 is null) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "sheetSha256");
        return await ApplySheetAsync(actor.Value, id, new(request.ExpectedRevision, Mode(request.Mode), request.SheetSha256,
            request.ConfirmDeactivations, request.ConfirmEmpty, request.ConfirmErrors),
            (_, ct) => spreadsheets.ReadAsync(request.Content, request.FileName, ct), cancellationToken);
    }

    // Replacing, as uploading a file does: in a catalog just created, without options, it is the same.
    public async Task<Result<FormCatalogImportPreviewResponse>> PreviewFromAssistantAsync(Guid id, FormCatalogAssistantImportRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(true, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        return await PreviewSheetAsync(id, request.ExpectedRevision!.Value, FormCatalogImportMode.Replace,
            (catalog, ct) => assistant.ReadAsync(actor.Value, catalog, request.RequestId!.Value, request.ProposalId!.Value, ct), cancellationToken);
    }

    public async Task<Result<FormCatalogSaveResponse>> ApplyFromAssistantAsync(Guid id, FormCatalogAssistantImportRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(true, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (request.SheetSha256 is null) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "sheetSha256");
        return await ApplySheetAsync(actor.Value, id, new(request.ExpectedRevision!.Value, FormCatalogImportMode.Replace, request.SheetSha256,
            request.ConfirmDeactivations, request.ConfirmEmpty, request.ConfirmErrors),
            (catalog, ct) => assistant.ReadAsync(actor.Value, catalog, request.RequestId!.Value, request.ProposalId!.Value, ct), cancellationToken);
    }

    // Nothing pending answers without data, never as not found: only a missing catalog is. Reading catalogs is enough to ask, but only
    // who can import has an import pending, as the turn's reserved permissions would say anyway.
    public async Task<Result<FormCatalogAssistantImportResponse>> GetAssistantImportAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(false, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var catalog = await reader.FindAsync(id, cancellationToken);
        var pending = catalog is null || !await access.HasPermissionAsync(actor.Value, BusinessFormsPermissions.CatalogsManage, cancellationToken)
            ? null : await assistant.FindPendingAsync(actor.Value, catalog, cancellationToken);
        var finalAccess = await access.CheckAsync(false, cancellationToken);
        if (finalAccess.IsFailure) return finalAccess.Error;
        return catalog is null ? FormCatalogErrors.NotFound : new FormCatalogAssistantImportResponse(id, pending);
    }

    // The review reads the sheet after the catalog's guards and saves nothing; it checks the revision again after reading every option.
    private async Task<Result<FormCatalogImportPreviewResponse>> PreviewSheetAsync(Guid id, long expectedRevision, FormCatalogImportMode mode,
        Func<FormCatalogStoredRow, CancellationToken, Task<Result<FormSpreadsheet>>> read, CancellationToken cancellationToken)
    {
        var current = await FindCatalogAsync(id, expectedRevision, cancellationToken);
        if (current.IsFailure) return current.Error;
        var sheet = await read(current.Value, cancellationToken);
        if (sheet.IsFailure)
        {
            var finalAccess = await access.CheckAsync(true, cancellationToken);
            return finalAccess.IsFailure ? finalAccess.Error : sheet.Error;
        }
        var items = await reader.ListAllItemsAsync(id, cancellationToken);
        var source = await imports.WithFieldsAsync(new FormCatalogImportSource(id, current.Value.Revision,
            Enum.Parse<BusinessFormCatalogValueType>(current.Value.ValueType), current.Value.OriginalCulture, items,
            current.Value.LastImportSha256, current.Value.LastImportAtUtc), current.Value.FieldsJson, cancellationToken);
        var plan = source.IsFailure ? Result.Failure<FormCatalogImportPlan>(source.Error)
            : FormCatalogImportPolicy.Build(source.Value, sheet.Value, mode, FormCatalogImportRecorder.Culture());
        var latest = await FindCatalogAsync(id, expectedRevision, cancellationToken);
        if (latest.IsFailure) return latest.Error;
        return plan.IsSuccess ? plan.Value.Preview : plan.Error;
    }

    // Applying reads the same sheet again, compares it with the reviewed one and records it in a single boundary.
    private async Task<Result<FormCatalogSaveResponse>> ApplySheetAsync(Guid actor, Guid id, FormCatalogImportApplication application,
        Func<FormCatalogStoredRow, CancellationToken, Task<Result<FormSpreadsheet>>> read, CancellationToken cancellationToken)
    {
        var current = await FindCatalogAsync(id, application.ExpectedRevision, cancellationToken);
        if (current.IsFailure) return current.Error;
        var sheet = await read(current.Value, cancellationToken);
        if (sheet.IsFailure) return sheet.Error;
        if (!string.Equals(application.SheetSha256, sheet.Value.Sha256, StringComparison.Ordinal)) return FormCatalogImportErrors.SheetChanged;
        var result = await unitOfWork.ExecuteInTransactionAsync(ct => imports.RecordAsync(id, current.Value.FieldsJson, application, sheet.Value, ct),
            CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsFailure) return result.Error;
        int? usageCount = await access.HasPermissionAsync(actor, BusinessFormsPermissions.Read, cancellationToken)
            ? await reader.CountUsageAsync(id, cancellationToken) : null;
        return result.Value with { Catalog = result.Value.Catalog with { UsageCount = usageCount } };
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
}
