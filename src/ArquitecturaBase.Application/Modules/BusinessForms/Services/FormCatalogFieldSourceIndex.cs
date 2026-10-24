using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogFieldSourceIndex
{
    private readonly Dictionary<Guid, FormCatalogStoredItemRow> _all;
    private readonly HashSet<Guid> _activeIds = [];
    private readonly Dictionary<string, Guid> _codes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid?> _texts = new(StringComparer.OrdinalIgnoreCase);

    public FormCatalogFieldSourceIndex(IReadOnlyList<FormCatalogStoredItemRow> items)
    {
        _all = items.ToDictionary(item => item.Id);
        foreach (var item in items.Where(item => item.IsActive))
        {
            _activeIds.Add(item.Id);
            _codes.Add(item.Code, item.Id);
            foreach (var culture in new[] { "es", "en" })
                if (item.TextIn(culture) is { } text) Add(text, item.Id);
        }
    }

    public bool Contains(Guid id) => _activeIds.Contains(id);

    // Any option of the source, active or deactivated: a saved value may keep an option that was deactivated later.
    public bool ContainsAny(Guid id) => _all.ContainsKey(id);

    // The text of any option of the source in a language, or in the other one when it has no translation; null for no option of the source.
    public string? Text(Guid id, string culture) => _all.TryGetValue(id, out var item) ? item.TextFor(culture) : null;

    // Active options resolve first; a cell that resolves to none of them but still names the saved option keeps it, even if it was deactivated later.
    public Result<string?> Resolve(string? input, Guid? current = null)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value)) return Result.Success<string?>(null);
        if (Guid.TryParseExact(value, "D", out var id) && _activeIds.Contains(id)) return id.ToString("D");
        if (_codes.TryGetValue(value, out id)) return id.ToString("D");
        if (_texts.TryGetValue(value, out var textId) && textId.HasValue) return textId.Value.ToString("D");
        return current is { } saved && Names(saved, value) ? saved.ToString("D") : FormCatalogErrors.FieldValueInvalid;
    }

    private bool Names(Guid id, string value) => _all.TryGetValue(id, out var item)
        && (Guid.TryParseExact(value, "D", out var named) && named == id
            || string.Equals(value, item.Code, StringComparison.OrdinalIgnoreCase)
            || item.TextIn("es") is { } spanish && string.Equals(value, spanish.Trim(), StringComparison.OrdinalIgnoreCase)
            || item.TextIn("en") is { } english && string.Equals(value, english.Trim(), StringComparison.OrdinalIgnoreCase));

    private void Add(string text, Guid id)
    {
        var key = text.Trim();
        if (_texts.TryGetValue(key, out var existing))
        {
            if (existing != id) _texts[key] = null;
        }
        else _texts.Add(key, id);
    }
}

internal static partial class FormCatalogFieldPolicy
{
    public static FormCatalogFieldSourceIndex CreateSourceIndex(IReadOnlyList<FormCatalogStoredItemRow> items) => new(items);
    public static Result<string?> ResolveCatalogValue(FormCatalogFieldSourceIndex source, string? input, Guid? current = null) => source.Resolve(input, current);
}
