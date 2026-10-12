using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogService(IFormCatalogRepository repository, IFormCatalogReader reader, IUnitOfWork unitOfWork,
    IRequestValidator validator, FormCatalogAccessGuard accessGuard) : IFormCatalogService
{
    public async Task<Result<FormCatalogSaveResponse>> CreateManualAsync(CreateManualCatalogRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        return await unitOfWork.ExecuteInTransactionAsync(ct => CreateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
    }

    public async Task<Result<FormCatalogSaveResponse>> UpdateManualAsync(Guid id, UpdateManualCatalogRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        return await unitOfWork.ExecuteInTransactionAsync(ct => UpdateCoreAsync(id, request, ct), CommitPolicy.OnSuccess, cancellationToken);
    }

    public async Task<Result<FormCatalogDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        var row = await reader.FindAsync(id, cancellationToken);
        return row is null ? FormCatalogErrors.NotFound : Map(row);
    }

    public async Task<Result<PagedResult<FormCatalogItemResponse>>> ListItemsAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await reader.FindAsync(id, cancellationToken) is null) return FormCatalogErrors.NotFound;
        if (page < 1 || pageSize is < 1 or > 100) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "page");
        return await reader.ListItemsAsync(id, page, pageSize, cancellationToken);
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
                detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), input.Position);
            if (added.IsFailure) return added.Error;
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
        var updated = catalog.UpdateManual(request.ExpectedRevision!.Value, RequiredTexts(request.Name!), RequiredTexts(request.Description!));
        if (updated.IsFailure) return updated.Error;

        var storedItems = await repository.ListItemsAsync(id, ct);
        var byId = storedItems.ToDictionary(item => item.Id);
        var requestedIds = request.Items!.Where(item => item!.ItemId.HasValue).Select(item => item!.ItemId!.Value).ToHashSet();
        if (requestedIds.Any(itemId => !byId.ContainsKey(itemId))) return FormCatalogErrors.ItemNotFound;
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
                    detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), value.Position);
                if (changed.IsFailure) return changed.Error;
                var active = catalog.SetManualItemActive(byId[itemId], value.IsActive);
                if (active.IsFailure) return active.Error;
            }
            else
            {
                var created = catalog.AddManualItem(value.Code!.Trim(), text["es"], text.GetValueOrDefault("en"),
                    detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), value.Position);
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

        repository.AddItems(addedItems);
        return new FormCatalogSaveResponse(Map(catalog, storedItems.Count + addedItems.Count), identityMap);
    }

    private static FormCatalogDetailResponse Map(FormCatalogStoredRow row) => new(row.Id, row.Key, ReadTexts(row.NameJson),
        ReadTexts(row.DescriptionJson), row.Kind, row.Revision, row.IsActive, row.ItemsCount, 0, null);

    private static FormCatalogDetailResponse Map(BusinessFormCatalog catalog, int itemCount) => new(catalog.Id, catalog.Key,
        ReadTexts(catalog.NameJson), ReadTexts(catalog.DescriptionJson), catalog.Kind.ToString(), catalog.Revision, catalog.IsActive,
        itemCount, 0, null);

    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog text is invalid.");

    private static Dictionary<string, string> RequiredTexts(IReadOnlyDictionary<string, string?> source) =>
        new(StringComparer.Ordinal) { ["es"] = source["es"]!, ["en"] = source["en"]! };

    private static Dictionary<string, string> Texts(IReadOnlyDictionary<string, string?> source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source.TryGetValue("es", out var spanish) && spanish is not null) result["es"] = spanish;
        if (source.TryGetValue("en", out var english) && english is not null) result["en"] = english;
        return result;
    }

    private static Dictionary<string, string> OptionalTexts(IReadOnlyDictionary<string, string?>? source)
    {
        if (source is null) return new(StringComparer.Ordinal);
        return Texts(source.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }
}
