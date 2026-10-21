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
    IRequestValidator validator, FormCatalogAccessGuard accessGuard, IUserReader users) : IFormCatalogService
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
        var catalog = BusinessFormCatalog.CreateManual(name, description);
        await repository.LockAsync(catalog.Id, ct);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<BusinessFormCatalogItem>();
        var identityMap = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (input, index) in request.Items!.Select((item, index) => (item!, index)))
        {
            if (!codes.Add(input.Code!.Trim())) return FieldErrors.On(FormCatalogErrors.DuplicateItemCode, $"items[{index}].code");
            var text = Texts(input.Text!);
            var detail = OptionalTexts(input.Detail);
            var added = catalog.AddManualItem(input.Code.Trim(), text["es"], text.GetValueOrDefault("en"),
                detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), items.Count + 1);
            if (added.IsFailure) return added.Error;
            if (!input.IsActive)
            {
                var inactive = catalog.SetManualItemActive(added.Value, false);
                if (inactive.IsFailure) return inactive.Error;
            }
            items.Add(added.Value);
            identityMap.Add(input.LocalId!, added.Value.Id);
        }

        repository.Add(catalog);
        repository.AddItems(items);
        return new FormCatalogSaveResponse(Map(catalog, items.Count), identityMap);
    }

    private async Task<Result<FormCatalogSaveResponse>> UpdateCoreAsync(Guid id, UpdateManualCatalogRequest request, CancellationToken ct)
    {
        await repository.LockAsync(id, ct);
        var catalog = await repository.GetAsync(id, ct);
        if (catalog is null) return FormCatalogErrors.NotFound;
        var updated = catalog.UpdateManual(request.ExpectedRevision!.Value, RequiredTexts(request.Name!), RequiredTexts(request.Description!), request.IsActive);
        if (updated.IsFailure) return updated.Error;

        var storedItems = await repository.ListItemsAsync(id, ct);
        var byId = storedItems.ToDictionary(item => item.Id);
        var requestedIds = request.Items!.Where(item => item!.ItemId.HasValue).Select(item => item!.ItemId!.Value).ToHashSet();
        if (requestedIds.Any(itemId => !byId.ContainsKey(itemId))) return FormCatalogErrors.ItemNotFound;
        var addedCount = request.Items!.Count(item => item!.ItemId is null);
        if (storedItems.Count + addedCount > 500) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "items");
        var nextPosition = storedItems.Count == 0 ? 1 : storedItems.Max(item => item.Position) + 1;
        var codes = new HashSet<string>(storedItems.Where(item => !requestedIds.Contains(item.Id))
            .Select(item => item.Code), StringComparer.OrdinalIgnoreCase);
        var addedItems = new List<BusinessFormCatalogItem>();
        var identityMap = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var input in request.Items!)
        {
            var value = input!;
            if (!codes.Add(value.Code!.Trim())) return FieldErrors.On(FormCatalogErrors.DuplicateItemCode, "items.code");
            var text = Texts(value.Text!);
            var detail = OptionalTexts(value.Detail);
            if (value.ItemId is { } itemId)
            {
                var changed = catalog.UpdateManualItem(byId[itemId], value.Code!.Trim(), text["es"], text.GetValueOrDefault("en"),
                    detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), byId[itemId].Position);
                if (changed.IsFailure) return changed.Error;
                var active = catalog.SetManualItemActive(byId[itemId], value.IsActive);
                if (active.IsFailure) return active.Error;
            }
            else
            {
                var created = catalog.AddManualItem(value.Code!.Trim(), text["es"], text.GetValueOrDefault("en"),
                    detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), nextPosition++);
                if (created.IsFailure) return created.Error;
                addedItems.Add(created.Value);
                identityMap.Add(value.LocalId!, created.Value.Id);
                if (!value.IsActive)
                {
                    var inactive = catalog.SetManualItemActive(created.Value, false);
                    if (inactive.IsFailure) return inactive.Error;
                }
            }
        }

        if (request.Moves is { } moves)
        {
            var orderedItems = storedItems.Concat(addedItems).OrderBy(item => item.Position).ThenBy(item => item.Id).ToList();
            for (var moveIndex = 0; moveIndex < moves.Count; moveIndex++)
            {
                var move = moves[moveIndex]!;
                var currentIndex = orderedItems.FindIndex(item => item.Id == move.ItemId);
                if (currentIndex < 0) return FormCatalogErrors.ItemNotFound;
                var targetIndex = move.Direction == "up" ? currentIndex - 1 : currentIndex + 1;
                if (targetIndex < 0 || targetIndex >= orderedItems.Count)
                    return FieldErrors.On(FormCatalogErrors.ContentInvalid, $"moves[{moveIndex}].direction");
                var item = orderedItems[currentIndex];
                orderedItems.RemoveAt(currentIndex);
                orderedItems.Insert(targetIndex, item);
            }
            for (var index = 0; index < orderedItems.Count; index++)
            {
                var orderedItem = orderedItems[index];
                if (orderedItem.Position == index + 1) continue;
                var repositioned = catalog.UpdateManualItem(orderedItem, orderedItem.Code, orderedItem.TextEs,
                    orderedItem.TextEn, orderedItem.DetailEs, orderedItem.DetailEn, index + 1);
                if (repositioned.IsFailure) return repositioned.Error;
            }
        }

        repository.AddItems(addedItems);
        return new FormCatalogSaveResponse(Map(catalog, storedItems.Count + addedItems.Count), identityMap);
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
        row.FiltersJson is null ? null : JsonSerializer.Deserialize<FormCatalogFilterDefinition>(row.FiltersJson, JsonSerializerOptions.Web));

    private static FormCatalogDetailResponse Map(BusinessFormCatalog catalog, int itemCount) => new(catalog.Id, catalog.Key,
        ReadTexts(catalog.NameJson), ReadTexts(catalog.DescriptionJson), catalog.Kind.ToString(), catalog.Revision, catalog.IsActive,
        itemCount, null, null);

    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog text is invalid.");

    private static Dictionary<string, string> RequiredTexts(IReadOnlyDictionary<string, string?> source) =>
        new(StringComparer.Ordinal) { ["es"] = source["es"]!, ["en"] = source["en"]! };

    private static Dictionary<string, string> Texts(IReadOnlyDictionary<string, string?> source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source.TryGetValue("es", out var spanish) && spanish is not null) result["es"] = spanish;
        if (source.TryGetValue("en", out var english) && !string.IsNullOrWhiteSpace(english)) result["en"] = english;
        return result;
    }

    private static Dictionary<string, string> OptionalTexts(IReadOnlyDictionary<string, string?>? source)
    {
        if (source is null) return new(StringComparer.Ordinal);
        return Texts(source.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }
}
