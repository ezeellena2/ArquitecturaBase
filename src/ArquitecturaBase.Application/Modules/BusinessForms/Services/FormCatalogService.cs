using System.Text.Json;
using System.Globalization;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogService(IFormCatalogRepository repository, IFormCatalogReader reader,
    IUnitOfWork unitOfWork,
    IRequestValidator validator, FormCatalogAccessGuard accessGuard, IUserReader users, FormCatalogItemRecorder recorder, FormCatalogFieldRecorder fieldRecorder) : IFormCatalogService
{
    public async Task<Result<FormCatalogSaveResponse>> CreateManualAsync(CreateManualCatalogRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var original = FormCatalogOriginalTextPolicy.Validate(Culture(), request.Name, request.Items, request.Fields);
        if (original.IsFailure) return original.Error;
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, cancellationToken);
        var result = await unitOfWork.ExecuteInTransactionAsync(ct => CreateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        return await WithUsageCountAsync(result, canReadForms, cancellationToken);
    }

    public async Task<Result<FormCatalogSaveResponse>> UpdateManualAsync(Guid id, UpdateManualCatalogRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, cancellationToken);
        var result = await unitOfWork.ExecuteInTransactionAsync(ct => UpdateCoreAsync(id, request, ct), CommitPolicy.OnSuccess, cancellationToken);
        return await WithUsageCountAsync(result, canReadForms, cancellationToken);
    }

    public async Task<Result<FormCatalogDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        var row = await reader.FindAsync(id, cancellationToken);
        if (row is null) return FormCatalogErrors.NotFound;
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, cancellationToken);
        int? usageCount = canReadForms ? await reader.CountUsageAsync(id, cancellationToken) : null;
        var detail = Map(row, usageCount, null) with { Proposal = await FormCatalogProposalPresentation.ReadAsync(
            row.ProposalEvidenceJson, row.ProposalState, row.ProposalDecidedByUserId, row.ProposalDecidedAtUtc, users, cancellationToken) };
        var items = row.FieldsJson is null && row.PresentationJson is null ? [] : await reader.ListAllItemsAsync(id, cancellationToken);
        return await FormCatalogFieldPresentation.DescribeAsync(detail, row.FieldsJson, row.PresentationJson, items, reader, canReadForms, cancellationToken);
    }

    public async Task<Result<FormCatalogsPageResponse>> ListAsync(ListFormCatalogsRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var localized = request with { DisplayCulture = Culture() };
        var canEdit = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.CatalogsManage, cancellationToken);
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, cancellationToken);
        return await reader.FindPageAsync(localized, canEdit, canReadForms, cancellationToken);
    }

    public async Task<Result<PagedResult<FormCatalogItemResponse>>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (await reader.FindAsync(id, cancellationToken) is null) return FormCatalogErrors.NotFound;
        var localized = request with { DisplayCulture = Culture() };
        return await reader.ListItemsAsync(id, localized, cancellationToken);
    }

    private async Task<Result<FormCatalogSaveResponse>> CreateCoreAsync(CreateManualCatalogRequest request, CancellationToken ct)
    {
        var name = FormCatalogProposalPolicy.Texts(request.Name);
        var description = FormCatalogProposalPolicy.Texts(request.Description);
        var catalog = BusinessFormCatalog.CreateManual(Culture(), name, description,
            Enum.Parse<BusinessFormCatalogValueType>(request.ValueType ?? "Text"));
        await LockCatalogsAsync(catalog.Id, request.Fields, ct);
        var access = await accessGuard.CheckAsync(true, ct);
        if (access.IsFailure) return access.Error;
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, ct);
        var fields = await fieldRecorder.PrepareAsync(catalog, [], request.Fields, request.Presentation, [], canReadForms, ct);
        if (fields.IsFailure) return fields.Error;
        var recorded = recorder.Record(catalog, [], request.Items!, fieldContext: fields.Value.Context);
        if (recorded.IsFailure) return recorded.Error;
        repository.Add(catalog);
        var detail = await FormCatalogFieldPresentation.DescribeAsync(Map(catalog, recorded.Value.Items.Count), catalog.FieldsJson,
            catalog.PresentationJson, FormCatalogFieldPresentation.ToRows(recorded.Value.Items), reader, canReadForms, ct, fields.Value.Context.Sources);
        return new FormCatalogSaveResponse(detail, recorded.Value.IdentityMap, fields.Value.IdentityMap);
    }

    private async Task<Result<FormCatalogSaveResponse>> UpdateCoreAsync(Guid id, UpdateManualCatalogRequest request, CancellationToken ct)
    {
        await LockCatalogsAsync(id, request.Fields, ct);
        var access = await accessGuard.CheckAsync(true, ct);
        if (access.IsFailure) return access.Error;
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, ct);
        var catalog = await repository.GetAsync(id, ct);
        if (catalog is null) return FormCatalogErrors.NotFound;
        if (catalog.Revision != request.ExpectedRevision) return FormCatalogErrors.RevisionConflict;
        var original = FormCatalogOriginalTextPolicy.Validate(catalog.OriginalCulture, request.Name, request.Items, request.Fields);
        if (original.IsFailure) return original.Error;
        if (request.IsActive == false && catalog.IsActive && await reader.ExistsSourceUsageAsync(id, ct)) return FormCatalogErrors.SourceInUse;
        var storedItems = await repository.ListItemsAsync(id, ct);
        var fields = await fieldRecorder.PrepareAsync(catalog, storedItems, request.Fields, request.Presentation, request.ConfirmFieldRemovals, canReadForms, ct);
        if (fields.IsFailure) return fields.Error;
        var recorded = recorder.Record(catalog, storedItems, request.Items!, request.Moves, request.SortAlphabetically, fields.Value.Context);
        if (recorded.IsFailure) return recorded.Error;
        var updated = catalog.UpdateManual(request.ExpectedRevision!.Value, FormCatalogProposalPolicy.Texts(request.Name), FormCatalogProposalPolicy.Texts(request.Description), request.IsActive);
        if (updated.IsFailure) return updated.Error;
        var detail = await FormCatalogFieldPresentation.DescribeAsync(Map(catalog, recorded.Value.Items.Count), catalog.FieldsJson,
            catalog.PresentationJson, FormCatalogFieldPresentation.ToRows(recorded.Value.Items), reader, canReadForms, ct, fields.Value.Context.Sources);
        return new FormCatalogSaveResponse(detail, recorded.Value.IdentityMap, fields.Value.IdentityMap);
    }

    private async Task LockCatalogsAsync(Guid id, IReadOnlyList<FormCatalogFieldSaveRequest?>? fields, CancellationToken ct)
    {
        var sourceIds = fields?.Where(field => field?.IsActive == true && field.SourceCatalogId is not null).Select(field => field!.SourceCatalogId!.Value) ?? [];
        foreach (var catalogId in sourceIds.Append(id).Distinct().Order()) await repository.LockAsync(catalogId, ct);
    }

    private async Task<Result<FormCatalogSaveResponse>> WithUsageCountAsync(Result<FormCatalogSaveResponse> result, bool canReadForms,
        CancellationToken cancellationToken)
    {
        if (result.IsFailure) return result.Error;
        var response = result.Value;
        int? usageCount = canReadForms ? await reader.CountUsageAsync(response.Catalog.Id, cancellationToken) : null;
        return response with { Catalog = response.Catalog with { UsageCount = usageCount } };
    }

    private static FormCatalogDetailResponse Map(FormCatalogStoredRow row, int? usageCount, string? unavailableReasonCode) => new(row.Id, row.Key, ReadTexts(row.NameJson),
        ReadTexts(row.DescriptionJson), row.OriginalCulture, row.Kind, row.Revision, row.IsActive, row.ItemsCount, usageCount, unavailableReasonCode,
        row.FiltersJson is null ? null : JsonSerializer.Deserialize<FormCatalogFilterDefinition>(row.FiltersJson, JsonSerializerOptions.Web), ValueType: row.ValueType);

    private static FormCatalogDetailResponse Map(BusinessFormCatalog catalog, int itemCount) => new(catalog.Id, catalog.Key,
        ReadTexts(catalog.NameJson), ReadTexts(catalog.DescriptionJson), catalog.OriginalCulture, catalog.Kind.ToString(), catalog.Revision, catalog.IsActive,
        itemCount, null, null, ValueType: catalog.ValueType.ToString());

    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog text is invalid.");

    private static string Culture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";

}
