using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static partial class FormCatalogImportPolicy
{
    private readonly record struct HeaderMatch(string? Key, bool Canonical = false, bool Ambiguous = false);

    private static HeaderMatch MatchHeader(string text, IReadOnlyList<BusinessFormCatalogField> fields)
    {
        var normalized = NormalizeHeader(text);
        if (normalized.Length == 0) return new(null);
        foreach (var field in fields)
            foreach (var culture in SupportedCultures)
            {
                if (normalized == NormalizeHeader(FormCatalogImportTemplate.FieldHeader(field, culture, true))) return new(field.Id.ToString("D"), true);
                var regular = FormCatalogImportTemplate.FieldHeader(field, culture, false);
                if (regular != field.Key && normalized == NormalizeHeader(regular)) return new(field.Id.ToString("D"));
            }
        if (FormCatalogImportTemplate.FixedHeaderKey(text) is { } fixedKey) return new(fixedKey);
        var byKey = fields.FirstOrDefault(field => normalized == NormalizeHeader(field.Key));
        if (byKey is not null) return new(byKey.Id.ToString("D"));
        var byName = fields.Where(field => field.Name.Values.Any(name => normalized == NormalizeHeader(name))).ToArray();
        return byName.Length switch { 0 => new(null), 1 => new(byName[0].Id.ToString("D")), _ => new(null, Ambiguous: true) };
    }

    private static Dictionary<Guid, Func<string?, Guid?, Result<string?>>> SourceResolvers(FormCatalogImportSource source)
    {
        var result = new Dictionary<Guid, Func<string?, Guid?, Result<string?>>>();
        foreach (var (id, options) in source.Sources ?? new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>())
        {
            var index = FormCatalogFieldPolicy.CreateSourceIndex(options);
            result.Add(id, (input, current) => FormCatalogFieldPolicy.ResolveCatalogValue(index, input, current));
        }
        return result;
    }

    private static Dictionary<string, JsonElement> ParseFields(IReadOnlyList<BusinessFormCatalogField> fields,
        Dictionary<Guid, Func<string?, Guid?, Result<string?>>> resolvers, FormCatalogStoredItemRow? existing,
        FormSpreadsheetRow row, Dictionary<string, int> columns, HashSet<string> canonicalFields,
        HashSet<string> invalidCells, bool active, string culture, List<FormCatalogImportIssue> issues)
    {
        var values = FormCatalogFieldPolicy.ReadValues(existing?.FieldValuesJson ?? "{}");
        foreach (var configured in fields)
        {
            var field = configured with { Required = configured.Required && active };
            var key = field.Id.ToString("D");
            if (invalidCells.Contains(key)) continue;
            if (!columns.ContainsKey(key))
            {
                if (field.Required && !values.ContainsKey(key)) issues.Add(Issue(row.Number, key, FormCatalogErrors.FieldValueRequired));
                continue;
            }
            var cell = Cell(row, columns, key);
            var input = cell?.Text;
            if (field.Type == "catalog")
            {
                // The saved option resolves even if it was deactivated later, so re-importing it is not a change.
                Guid? saved = values.TryGetValue(key, out var stored) && Guid.TryParse(FormCatalogFieldPolicy.Input(stored), out var savedId) ? savedId : null;
                Result<string?> resolved = string.IsNullOrWhiteSpace(input) ? Result.Success<string?>(null)
                    : field.SourceCatalogId is { } sourceId && resolvers.TryGetValue(sourceId, out var resolve)
                        ? resolve(input, saved) : FormCatalogErrors.FieldValueInvalid;
                if (resolved.IsFailure) { issues.Add(Issue(row.Number, key, resolved.Error)); continue; }
                input = resolved.Value;
            }
            var parsed = FormCatalogFieldPolicy.NormalizeImport(field, input, culture, canonicalFields.Contains(key) || cell?.IsCanonicalValue == true);
            if (parsed.IsFailure) issues.Add(Issue(row.Number, key, parsed.Error));
            else if (parsed.Value is { } value) values[key] = value;
            else values.Remove(key);
        }
        return values;
    }

    private static void ValidateUniqueFields(FormCatalogImportSource source, IReadOnlyList<BusinessFormCatalogField> fields,
        List<FormCatalogImportParsedRow> rows, FormCatalogImportMode mode, HashSet<string?> present,
        List<FormCatalogImportIssue> issues, HashSet<int> errorRows)
    {
        var unique = fields.Where(field => field.Unique).Select(field => field.Id.ToString("D")).ToArray();
        if (unique.Length == 0) return;
        var originals = source.Items.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
        var imported = rows.ToDictionary(row => row.Code, StringComparer.OrdinalIgnoreCase);
        var states = source.Items.ToDictionary(item => item.Code,
            item => (Active: item.IsActive && (mode == FormCatalogImportMode.Append || present.Contains(item.Code)),
                Values: (IReadOnlyDictionary<string, JsonElement>)FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson)), StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) states[row.Code] = (row.Active, row.Fields);
        var owners = unique.ToDictionary(key => key, _ => new Dictionary<string, HashSet<string>>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (code, state) in states) Add(code, state);
        var pending = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        var invalid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in owners)
            foreach (var codes in values.Values.Where(codes => codes.Count > 1)) Enqueue(key, codes);
        while (queue.TryDequeue(out var code))
        {
            if (!pending.Remove(code, out var keys) || !invalid.Add(code)) continue;
            var row = imported[code];
            foreach (var key in keys) issues.Add(Issue(row.Number, key, FormCatalogErrors.FieldValueDuplicate));
            errorRows.Add(row.Number);
            var state = states[code];
            foreach (var key in unique)
                if (state.Active && state.Values.TryGetValue(key, out var raw) && FormCatalogFieldPolicy.Input(raw) is { } text) owners[key][text].Remove(code);
            states.Remove(code);
            if (!originals.TryGetValue(code, out var original)) continue;
            var restored = (original.IsActive, (IReadOnlyDictionary<string, JsonElement>)FormCatalogFieldPolicy.ReadValues(original.FieldValuesJson));
            states.Add(code, restored);
            Add(code, restored);
            foreach (var key in unique)
                if (restored.IsActive && restored.Item2.TryGetValue(key, out var raw) && FormCatalogFieldPolicy.Input(raw) is { } text) Enqueue(key, owners[key][text]);
        }
        rows.RemoveAll(row => invalid.Contains(row.Code));

        // Saved values come from jsonb unescaped and imported ones from the serializer escaped: owners compare the decoded value.
        void Add(string code, (bool Active, IReadOnlyDictionary<string, JsonElement> Values) state)
        {
            if (!state.Active) return;
            foreach (var key in unique)
                if (state.Values.TryGetValue(key, out var raw) && FormCatalogFieldPolicy.Input(raw) is { } text)
                {
                    if (!owners[key].TryGetValue(text, out var codes)) owners[key].Add(text, codes = new(StringComparer.OrdinalIgnoreCase));
                    codes.Add(code);
                }
        }

        void Enqueue(string key, HashSet<string> codes)
        {
            if (codes.Count < 2) return;
            foreach (var code in codes.Where(code => imported.ContainsKey(code) && !invalid.Contains(code)))
            {
                if (!pending.TryGetValue(code, out var keys)) { pending.Add(code, keys = new(StringComparer.Ordinal)); queue.Enqueue(code); }
                keys.Add(key);
            }
        }
    }
}
