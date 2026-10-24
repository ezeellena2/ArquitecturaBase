using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogItemRecorder(IFormCatalogRepository repository)
{
    public Result<FormCatalogItemsRecorded> Record(BusinessFormCatalog catalog,
        IReadOnlyList<BusinessFormCatalogItem> storedItems, IReadOnlyList<FormCatalogManualItemSaveRequest?> inputs,
        IReadOnlyList<FormCatalogManualItemMoveRequest?>? moves = null, bool sortAlphabetically = false, FormCatalogFieldContext? fieldContext = null)
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
        var requested = new Dictionary<Guid, Dictionary<string, JsonElement>>();
        foreach (var (value, index) in inputs.Select((item, index) => (item!, index)))
        {
            if (!codes.Add(value.Code!.Trim())) return FieldErrors.On(FormCatalogErrors.DuplicateItemCode, $"items[{index}].code");
            var itemValue = FormCatalogValuePolicy.Normalize(catalog.ValueType, value.Value);
            if (itemValue.IsFailure) return FieldErrors.On(itemValue.Error, $"items[{index}].value");
            var text = Texts(value.Text!);
            var detail = OptionalTexts(value.Detail);
            BusinessFormCatalogItem recordedItem;
            if (value.ItemId is { } itemId)
            {
                recordedItem = byId[itemId];
                var changed = catalog.UpdateManualItem(byId[itemId], value.Code!.Trim(), text.GetValueOrDefault("es"), text.GetValueOrDefault("en"),
                    detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), byId[itemId].Position, itemValue.Value);
                if (changed.IsFailure) return changed.Error;
                var active = catalog.SetManualItemActive(byId[itemId], value.IsActive);
                if (active.IsFailure) return active.Error;
            }
            else
            {
                var created = catalog.AddManualItem(value.Code!.Trim(), text.GetValueOrDefault("es"), text.GetValueOrDefault("en"),
                    detail.GetValueOrDefault("es"), detail.GetValueOrDefault("en"), nextPosition++, itemValue.Value);
                if (created.IsFailure) return created.Error;
                recordedItem = created.Value;
                addedItems.Add(created.Value);
                identityMap.Add(value.LocalId!, created.Value.Id);
                if (!value.IsActive)
                {
                    var inactive = catalog.SetManualItemActive(created.Value, false);
                    if (inactive.IsFailure) return inactive.Error;
                }
            }
            if (value.Fields is not null)
            {
                var definitions = (fieldContext?.Fields ?? []).ToDictionary(field => field.Id.ToString("D"), StringComparer.Ordinal);
                var storedValues = FormCatalogFieldPolicy.ReadValues(recordedItem.FieldValuesJson);
                // Fields is a delta: an absent key keeps its stored value and null clears it.
                var newValues = new Dictionary<string, JsonElement>(storedValues, StringComparer.Ordinal);
                foreach (var (key, inputValue) in value.Fields)
                {
                    if (!definitions.TryGetValue(key, out var field)) return FieldErrors.On(FormCatalogErrors.FieldInvalid, $"items[{index}].fields.{key}");
                    if (!field.IsActive)
                    {
                        if (!storedValues.TryGetValue(key, out var old) || FormCatalogFieldPolicy.Input(old) != inputValue)
                            return FieldErrors.On(FormCatalogErrors.FieldInvalid, $"items[{index}].fields.{key}");
                        continue;
                    }
                    newValues[key] = JsonSerializer.SerializeToElement(inputValue, JsonSerializerOptions.Web);
                }
                requested[recordedItem.Id] = newValues;
            }
        }

        if (sortAlphabetically || moves is not null)
        {
            // A-Z follows the catalog's original text, compared with the rules of that language.
            var orderedItems = sortAlphabetically
                ? storedItems.Concat(addedItems).OrderBy(item => item.TextFor(catalog.OriginalCulture),
                        StringComparer.Create(CultureInfo.GetCultureInfo(catalog.OriginalCulture == "en" ? "en-US" : "es-AR"), false))
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

        var allItems = storedItems.Concat(addedItems).ToArray();
        if (fieldContext is { Fields.Count: > 0 })
        {
            var validationInputs = inputs.Select(input => input!.ItemId is null ? input with { ItemId = identityMap[input.LocalId!] } : input).ToArray();
            var validated = FormCatalogFieldPolicy.ValidateItems(catalog, allItems, validationInputs, requested, fieldContext);
            if (validated.IsFailure) return validated.Error;
        }
        repository.AddItems(addedItems);
        return new FormCatalogItemsRecorded(identityMap, allItems);
    }

    // A blank text is no text: the catalog requires its original and the other culture is an optional translation.
    private static Dictionary<string, string> Texts(IReadOnlyDictionary<string, string?> source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var culture in new[] { "es", "en" })
            if (source.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text)) result[culture] = text;
        return result;
    }

    private static Dictionary<string, string> OptionalTexts(IReadOnlyDictionary<string, string?>? source)
    {
        if (source is null) return new(StringComparer.Ordinal);
        return Texts(source.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }
}
