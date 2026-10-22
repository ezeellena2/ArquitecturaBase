using System.Globalization;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogItemRecorder(IFormCatalogRepository repository)
{
    public Result<IReadOnlyDictionary<string, Guid>> Record(BusinessFormCatalog catalog,
        IReadOnlyList<BusinessFormCatalogItem> storedItems, IReadOnlyList<FormCatalogManualItemSaveRequest?> inputs,
        IReadOnlyList<FormCatalogManualItemMoveRequest?>? moves = null, bool sortAlphabetically = false)
    {
        var byId = storedItems.ToDictionary(item => item.Id);
        var requestedIds = inputs.Where(item => item!.ItemId.HasValue).Select(item => item!.ItemId!.Value).ToHashSet();
        if (requestedIds.Any(itemId => !byId.ContainsKey(itemId))) return FormCatalogErrors.ItemNotFound;
        var addedCount = inputs.Count(item => item!.ItemId is null);
        if (storedItems.Count + addedCount > BusinessFormCatalog.MaxItems) return FieldErrors.On(FormCatalogErrors.TooManyItems, "items");
        var nextPosition = storedItems.Count == 0 ? 1 : storedItems.Max(item => item.Position) + 1;
        var codes = new HashSet<string>(storedItems.Where(item => !requestedIds.Contains(item.Id))
            .Select(item => item.Code), StringComparer.OrdinalIgnoreCase);
        var addedItems = new List<BusinessFormCatalogItem>();
        var identityMap = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (value, index) in inputs.Select((item, index) => (item!, index)))
        {
            if (!codes.Add(value.Code!.Trim())) return FieldErrors.On(FormCatalogErrors.DuplicateItemCode, $"items[{index}].code");
            var itemValue = FormCatalogValuePolicy.Normalize(catalog.ValueType, value.Value);
            if (itemValue.IsFailure) return FieldErrors.On(itemValue.Error, $"items[{index}].value");
            var text = Texts(value.Text!);
            var detail = OptionalTexts(value.Detail);
            if (value.ItemId is { } itemId)
            {
                var changed = catalog.UpdateManualItem(byId[itemId], value.Code!.Trim(), text["es"], text.GetValueOrDefault("en"),
                    detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), byId[itemId].Position, itemValue.Value);
                if (changed.IsFailure) return changed.Error;
                var active = catalog.SetManualItemActive(byId[itemId], value.IsActive);
                if (active.IsFailure) return active.Error;
            }
            else
            {
                var created = catalog.AddManualItem(value.Code!.Trim(), text["es"], text.GetValueOrDefault("en"),
                    detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), nextPosition++, itemValue.Value);
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

        if (sortAlphabetically || moves is not null)
        {
            var orderedItems = sortAlphabetically
                ? storedItems.Concat(addedItems).OrderBy(item => item.TextEs, StringComparer.Create(CultureInfo.GetCultureInfo("es-AR"), false))
                    .ThenBy(item => item.Position).ThenBy(item => item.Id).ToList()
                : storedItems.Concat(addedItems).OrderBy(item => item.Position).ThenBy(item => item.Id).ToList();
            var requestedMoves = moves ?? [];
            for (var moveIndex = 0; moveIndex < requestedMoves.Count; moveIndex++)
            {
                var move = requestedMoves[moveIndex]!;
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
                    orderedItem.TextEn, orderedItem.DetailEs, orderedItem.DetailEn, index + 1, orderedItem.Value);
                if (repositioned.IsFailure) return repositioned.Error;
            }
        }

        repository.AddItems(addedItems);
        return identityMap;
    }

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
