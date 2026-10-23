using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogFieldSourceIndex
{
    private readonly HashSet<Guid> _activeIds = [];
    private readonly Dictionary<string, Guid> _codes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid?> _texts = new(StringComparer.OrdinalIgnoreCase);

    public FormCatalogFieldSourceIndex(IReadOnlyList<FormCatalogStoredItemRow> items)
    {
        foreach (var item in items.Where(item => item.IsActive))
        {
            _activeIds.Add(item.Id);
            _codes.Add(item.Code, item.Id);
            Add(item.TextEs, item.Id);
            if (!string.IsNullOrWhiteSpace(item.TextEn)) Add(item.TextEn, item.Id);
        }
    }

    public bool Contains(Guid id) => _activeIds.Contains(id);

    public Result<string?> Resolve(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value)) return Result.Success<string?>(null);
        if (Guid.TryParseExact(value, "D", out var id) && _activeIds.Contains(id)) return id.ToString("D");
        if (_codes.TryGetValue(value, out id)) return id.ToString("D");
        return _texts.TryGetValue(value, out var textId) && textId.HasValue ? textId.Value.ToString("D") : FormCatalogErrors.FieldValueInvalid;
    }

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
    public static Result<string?> ResolveCatalogValue(FormCatalogFieldSourceIndex source, string? input) => source.Resolve(input);
}
