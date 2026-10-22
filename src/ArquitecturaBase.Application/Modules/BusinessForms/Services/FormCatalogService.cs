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
    IRequestValidator validator, FormCatalogAccessGuard accessGuard, IUserReader users, FormCatalogItemRecorder recorder) : IFormCatalogService
{
    public async Task<Result<FormCatalogSaveResponse>> CreateManualAsync(CreateManualCatalogRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
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
        return Map(row, usageCount, null) with { Proposal = await FormCatalogProposalPresentation.ReadAsync(
            row.ProposalEvidenceJson, row.ProposalState, row.ProposalDecidedByUserId, row.ProposalDecidedAtUtc, users, cancellationToken) };
    }

    public async Task<Result<FormCatalogsPageResponse>> ListAsync(ListFormCatalogsRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var localized = request with { DisplayCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es" };
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
        var localized = request with { DisplayCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es" };
        return await reader.ListItemsAsync(id, localized, cancellationToken);
    }

    private async Task<Result<FormCatalogSaveResponse>> CreateCoreAsync(CreateManualCatalogRequest request, CancellationToken ct)
    {
        var name = RequiredTexts(request.Name!);
        var description = RequiredTexts(request.Description!);
        var catalog = BusinessFormCatalog.CreateManual(name, description,
            Enum.Parse<BusinessFormCatalogValueType>(request.ValueType ?? "Text"));
        await repository.LockAsync(catalog.Id, ct);
        var recorded = recorder.Record(catalog, [], request.Items!);
        if (recorded.IsFailure) return recorded.Error;
        repository.Add(catalog);
        return new FormCatalogSaveResponse(Map(catalog, recorded.Value.Count), recorded.Value);
    }

    private async Task<Result<FormCatalogSaveResponse>> UpdateCoreAsync(Guid id, UpdateManualCatalogRequest request, CancellationToken ct)
    {
        await repository.LockAsync(id, ct);
        var catalog = await repository.GetAsync(id, ct);
        if (catalog is null) return FormCatalogErrors.NotFound;
        var updated = catalog.UpdateManual(request.ExpectedRevision!.Value, RequiredTexts(request.Name!), RequiredTexts(request.Description!), request.IsActive);
        if (updated.IsFailure) return updated.Error;

        var storedItems = await repository.ListItemsAsync(id, ct);
        var recorded = recorder.Record(catalog, storedItems, request.Items!, request.Moves, request.SortAlphabetically);
        if (recorded.IsFailure) return recorded.Error;
        return new FormCatalogSaveResponse(Map(catalog, storedItems.Count + recorded.Value.Count), recorded.Value);
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
        ReadTexts(row.DescriptionJson), row.Kind, row.Revision, row.IsActive, row.ItemsCount, usageCount, unavailableReasonCode,
        row.FiltersJson is null ? null : JsonSerializer.Deserialize<FormCatalogFilterDefinition>(row.FiltersJson, JsonSerializerOptions.Web), ValueType: row.ValueType);

    private static FormCatalogDetailResponse Map(BusinessFormCatalog catalog, int itemCount) => new(catalog.Id, catalog.Key,
        ReadTexts(catalog.NameJson), ReadTexts(catalog.DescriptionJson), catalog.Kind.ToString(), catalog.Revision, catalog.IsActive,
        itemCount, null, null, ValueType: catalog.ValueType.ToString());

    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog text is invalid.");

    private static Dictionary<string, string> RequiredTexts(IReadOnlyDictionary<string, string?> source) =>
        new(StringComparer.Ordinal) { ["es"] = source["es"]!, ["en"] = source["en"]! };

}
