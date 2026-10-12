using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogFiltersService(
    IFormCatalogRepository repository,
    IFormCatalogFilterReader filterReader,
    IUnitOfWork unitOfWork,
    IRequestValidator validator,
    FormCatalogAccessGuard accessGuard) : IFormCatalogFiltersService
{
    public async Task<Result<FormCatalogFilterOptionsResponse>> GetOptionsAsync(
        FormCatalogFilterOptionsRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;

        var sourceAvailable = await filterReader.ExistsAvailableSourceAsync(cancellationToken);
        IReadOnlyList<string> roleNames = sourceAvailable
            ? await filterReader.ListRoleNamesAsync(cancellationToken)
            : Array.Empty<string>();
        var columns = new[]
        {
            new FormCatalogFilterColumnResponse("name", "text", true, true, true, ["contains", "equals"], null),
            new FormCatalogFilterColumnResponse("role", "text", true, true, true, ["has"],
                roleNames.Select(role => new FormCatalogFilterChoiceResponse(role, role)).ToArray()),
            new FormCatalogFilterColumnResponse("active", "boolean", true, true, true, ["equals"],
                [new("true", FormCatalogTexts.Active), new("false", FormCatalogTexts.Inactive)]),
        };
        var canManage = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.CatalogsManage, cancellationToken);
        var sampleUsers = sourceAvailable && canManage
            ? await filterReader.ListSampleUsersAsync(request, cancellationToken)
            : null;
        return new FormCatalogFilterOptionsResponse("users", columns, sampleUsers, sourceAvailable,
            sourceAvailable ? null : FormCatalogErrors.SourceUnavailable.Code);
    }

    public async Task<Result<FormCatalogFilterPreviewResponse>> PreviewAsync(
        FormCatalogFilterPreviewRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (!await filterReader.ExistsAvailableSourceAsync(cancellationToken)) return FormCatalogErrors.SourceUnavailable;
        if (!await filterReader.ExistsActiveSampleUserAsync(request.SampleUserId, cancellationToken))
            return FormCatalogErrors.NotFound;
        var preview = await filterReader.FindPreviewAsync(request.SampleUserId, request.Filters!, cancellationToken);
        return preview with
        {
            Items = preview.Items.Select(item => new FormCatalogFilterOptionResponse(item.Id,
                request.Filters!.TextColumn == "active" ? FormCatalogTexts.ActiveValue(item.Text) : item.Text,
                request.Filters.DetailColumn == "active" && item.Detail is not null
                    ? FormCatalogTexts.ActiveValue(item.Detail) : item.Detail)).ToArray(),
        };
    }

    public async Task<Result<FormCatalogSaveResponse>> CreateAsync(
        CreateFiltersCatalogRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (!await filterReader.ExistsAvailableSourceAsync(cancellationToken)) return FormCatalogErrors.SourceUnavailable;
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, cancellationToken);
        var filtersJson = JsonSerializer.Serialize(request.Filters, JsonSerializerOptions.Web);
        var created = await unitOfWork.ExecuteInTransactionAsync<Result<FormCatalogSaveResponse>>(async ct =>
        {
            var catalog = BusinessFormCatalog.CreateFilters(RequiredTexts(request.Name!), RequiredTexts(request.Description!), filtersJson);
            await repository.LockAsync(catalog.Id, ct);
            repository.Add(catalog);
            return Result<FormCatalogSaveResponse>.Success(
                new FormCatalogSaveResponse(Map(catalog, request.Filters!, canReadForms), EmptyIdentityMap()));
        }, CommitPolicy.OnSuccess, cancellationToken);
        return created;
    }

    public async Task<Result<FormCatalogSaveResponse>> UpdateAsync(
        Guid id, UpdateFiltersCatalogRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (!await filterReader.ExistsAvailableSourceAsync(cancellationToken)) return FormCatalogErrors.SourceUnavailable;
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, cancellationToken);
        var filtersJson = JsonSerializer.Serialize(request.Filters, JsonSerializerOptions.Web);
        var updated = await unitOfWork.ExecuteInTransactionAsync<Result<FormCatalogSaveResponse>>(async ct =>
        {
            await repository.LockAsync(id, ct);
            var catalog = await repository.GetAsync(id, ct);
            if (catalog is null) return FormCatalogErrors.NotFound;
            var result = catalog.UpdateFilters(request.ExpectedRevision!.Value, RequiredTexts(request.Name!),
                RequiredTexts(request.Description!), filtersJson);
            if (result.IsFailure) return result.Error;
            return Result<FormCatalogSaveResponse>.Success(new FormCatalogSaveResponse(
                Map(catalog, request.Filters!, canReadForms), EmptyIdentityMap()));
        }, CommitPolicy.OnSuccess, cancellationToken);
        return updated;
    }

    private static FormCatalogDetailResponse Map(BusinessFormCatalog catalog, FormCatalogFilterDefinition filters,
        bool canReadForms) => new(catalog.Id, catalog.Key, ReadTexts(catalog.NameJson), ReadTexts(catalog.DescriptionJson),
        catalog.Kind.ToString(), catalog.Revision, catalog.IsActive, 0, canReadForms ? 0 : null, null, filters);

    private static Dictionary<string, Guid> EmptyIdentityMap() => new(StringComparer.Ordinal);

    private static Dictionary<string, string> RequiredTexts(IReadOnlyDictionary<string, string?> values) =>
        new(StringComparer.Ordinal) { ["es"] = values["es"]!, ["en"] = values["en"]! };

    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog text is invalid.");
}
