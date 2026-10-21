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
    FormCatalogAccessGuard accessGuard, IUserReader users, IFormCatalogReader catalogReader) : IFormCatalogFiltersService
{
    public async Task<Result<FormCatalogFilterOptionsResponse>> GetOptionsAsync(
        FormCatalogFilterOptionsRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;

        var roles = await filterReader.ListRolesAsync(cancellationToken);
        var columns = new[]
        {
            new FormCatalogFilterColumnResponse("name", "text", true, true, true, ["contains", "equals"], null),
            new FormCatalogFilterColumnResponse("role", "text", true, true, true, ["has"],
                roles),
            new FormCatalogFilterColumnResponse("active", "boolean", true, true, true, ["equals"],
                [new("true", FormCatalogTexts.Active), new("false", FormCatalogTexts.Inactive)]),
        };
        var canManage = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.CatalogsManage, cancellationToken);
        var sampleUsers = canManage
            ? await filterReader.ListSampleUsersAsync(request, cancellationToken)
            : null;
        return new FormCatalogFilterOptionsResponse("users", columns, sampleUsers);
    }

    public async Task<Result<FormCatalogFilterPreviewResponse>> PreviewAsync(
        FormCatalogFilterPreviewRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
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
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, cancellationToken);
        var filtersJson = JsonSerializer.Serialize(request.Filters, JsonSerializerOptions.Web);
        var created = await unitOfWork.ExecuteInTransactionAsync<Result<FormCatalogSaveResponse>>(async ct =>
        {
            var catalog = BusinessFormCatalog.CreateFilters(RequiredTexts(request.Name!), RequiredTexts(request.Description!), filtersJson);
            await repository.LockAsync(catalog.Id, ct);
            repository.Add(catalog);
            return Result<FormCatalogSaveResponse>.Success(
                new FormCatalogSaveResponse(Map(catalog, request.Filters!, canReadForms ? 0 : null), EmptyIdentityMap()));
        }, CommitPolicy.OnSuccess, cancellationToken);
        return created;
    }

    public async Task<Result<FormCatalogSaveResponse>> UpdateAsync(
        Guid id, UpdateFiltersCatalogRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (request.ExpectedRevision is null or < 1 or > 9007199254740991)
            return FieldErrors.On(FormCatalogErrors.ContentInvalid, "expectedRevision");
        var canReadForms = await accessGuard.HasPermissionAsync(access.Value, BusinessFormsPermissions.Read, cancellationToken);
        return await unitOfWork.ExecuteInTransactionAsync<Result<FormCatalogSaveResponse>>(async ct =>
        {
            await repository.LockAsync(id, ct);
            var catalog = await repository.GetAsync(id, ct);
            if (catalog is null) return FormCatalogErrors.NotFound;
            if (catalog.ProposalEvidenceJson is { } evidenceJson)
            {
                var evidence = FormCatalogProposalPresentation.Evidence(evidenceJson);
                var content = FormCatalogProposalPolicy.ValidateEdits(request.Name, request.Description, request.Filters, evidence.OriginalCulture);
                if (content.IsFailure) return content.Error;
                var roles = FormCatalogProposalPolicy.ValidateRoles(request.Filters!, await filterReader.ListRolesAsync(ct));
                if (roles.IsFailure) return roles.Error;
            }
            else if (await validator.ValidateAsync(request, ct) is { } invalid) return invalid;
            var result = catalog.UpdateFilters(request.ExpectedRevision.Value, FormCatalogProposalPolicy.Texts(request.Name!),
                FormCatalogProposalPolicy.Texts(request.Description!), JsonSerializer.Serialize(request.Filters, JsonSerializerOptions.Web), request.IsActive);
            if (result.IsFailure) return result.Error;
            int? usageCount = canReadForms ? await catalogReader.CountUsageAsync(catalog.Id, ct) : null;
            var detail = Map(catalog, request.Filters!, usageCount) with
            {
                Proposal = await FormCatalogProposalPresentation.ReadAsync(catalog.ProposalEvidenceJson, catalog.ProposalState,
                    catalog.ProposalDecidedByUserId, catalog.ProposalDecidedAtUtc, users, ct)
            };
            return new FormCatalogSaveResponse(detail, EmptyIdentityMap());
        }, CommitPolicy.OnSuccess, cancellationToken);
    }

    private static FormCatalogDetailResponse Map(BusinessFormCatalog catalog, FormCatalogFilterDefinition filters,
        int? usageCount) => new(catalog.Id, catalog.Key, ReadTexts(catalog.NameJson), ReadTexts(catalog.DescriptionJson),
        catalog.Kind.ToString(), catalog.Revision, catalog.IsActive, 0, usageCount, null, filters);

    private static Dictionary<string, Guid> EmptyIdentityMap() => new(StringComparer.Ordinal);

    private static Dictionary<string, string> RequiredTexts(IReadOnlyDictionary<string, string?> values) =>
        new(StringComparer.Ordinal) { ["es"] = values["es"]!, ["en"] = values["en"]! };

    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog text is invalid.");
}
