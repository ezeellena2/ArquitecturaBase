using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// The items of a block from the keys a Flow r10 sends: the questions of item n under <c>&lt;key&gt;_r&lt;n&gt;</c>, «¿Agregás otro
/// ítem?» under <c>&lt;key of the block&gt;_r&lt;n&gt;</c> where the item asks, and «¿Agregás un ítem?» under <c>_r0</c> with a
/// minimum of 0. Reads <see cref="WhatsAppFlowScreenBuilder.Asks"/> and <see cref="WhatsAppFlowScreenBuilder.RowCount"/>
/// without changing them and returns the owner's shape: a JSON array with an object per item and its canonical values.
/// </summary>
internal static class WhatsAppFlowRowsCodec
{
    private static readonly string[] ItemTypes = ["shortText", "longText", "decimal", "date", "boolean", "choice"];

    /// <summary>At most one block: a group with valid rows followed, without gaps, by its questions of the types an item takes.</summary>
    public static bool IsWellFormed(IReadOnlyList<WhatsAppFlowProjectionField> mapping)
    {
        var groups = Enumerable.Range(0, mapping.Count).Where(index => mapping[index].Type == "group").ToArray();
        if (groups.Length == 0) return mapping.All(field => field.Repeated != true && field.Rows is null);
        if (groups.Length > 1) return false;
        var at = groups[0];
        var end = at + 1;
        while (end < mapping.Count && mapping[end].Repeated == true) end++;
        return mapping[at].Rows is { MinRows: >= 0 and <= 100, MaxRows: >= 1 and <= 100 } rows && rows.MinRows <= rows.MaxRows
            && mapping[at].Repeated != true && end > at + 1
            && Enumerable.Range(0, mapping.Count).All(index => index == at
                || (index > at && index < end ? ItemTypes.Contains(mapping[index].Type) : mapping[index].Repeated != true) && mapping[index].Rows is null);
    }

    /// <summary>A key of an item or of a decision of the block: it never reaches the owner as a loose answer.</summary>
    public static bool IsRowKey(IReadOnlyList<WhatsAppFlowProjectionField> mapping, string key) =>
        Suffix(key) is { } suffix && mapping.Any(field => field.Key == suffix.Name && (field.Type == "group" || field.Repeated == true));

    /// <summary>
    /// The items of the block (null without a block). A question without its suffix, a suffix on a question outside the
    /// block, a required question missing in a loaded item or a missing decision are not something the Flow sends; what
    /// arrives from items after a «No» (back with the arrow, then «No») is ignored.
    /// </summary>
    public static Result<string?> Decode(IReadOnlyList<WhatsAppFlowProjectionField> mapping, IReadOnlyDictionary<string, string> answers,
        IReadOnlyList<StructuredFormCatalogOffer> catalogs)
    {
        var at = Enumerable.Range(0, mapping.Count).FirstOrDefault(index => mapping[index].Type == "group", -1);
        if (at < 0) return Result.Success<string?>(null);
        var group = mapping[at];
        var rows = group.Rows!;
        var block = mapping.Skip(at + 1).TakeWhile(field => field.Repeated == true).ToArray();
        foreach (var key in answers.Keys)
        {
            if (key == group.Key || block.Any(field => field.Key == key)) return FlowErrors.InvalidValue;
            if (Suffix(key) is not { } suffix) continue;
            if (suffix.Name == group.Key ? suffix.Row == 0 && rows.MinRows > 0
                : block.Any(field => field.Key == suffix.Name) ? suffix.Row == 0
                : mapping.Any(field => field.Key == suffix.Name)) return FlowErrors.InvalidValue;
        }

        var count = WhatsAppFlowScreenBuilder.RowCount(rows);
        var row = 1;
        if (rows.MinRows == 0)
        {
            if (Decision(answers, group.Key, 0) is not { } start) return FlowErrors.InvalidValue;
            if (!start) row = count + 1;
        }
        var items = new List<Dictionary<string, string>>();
        for (; row <= count; row++)
        {
            var item = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in block)
            {
                answers.TryGetValue(field.Key + "_r" + row.ToString(CultureInfo.InvariantCulture), out var raw);
                var value = WhatsAppFlowValueCodec.Decode(WhatsAppFlowValueCodec.ForReply(field, catalogs), raw);
                if (value.IsFailure) return FlowErrors.InvalidValue;
                if (WhatsAppFlowValueCodec.Canonical(value.Value) is { } canonical) item[field.Key] = canonical;
            }
            items.Add(item);
            var answered = answers.ContainsKey(group.Key + "_r" + row.ToString(CultureInfo.InvariantCulture));
            if (!WhatsAppFlowScreenBuilder.Asks(rows, row))
            {
                if (answered) return FlowErrors.InvalidValue;
                continue;
            }
            if (Decision(answers, group.Key, row) is not { } more) return FlowErrors.InvalidValue;
            if (!more) break;
        }
        return JsonSerializer.Serialize(items);
    }

    private static bool? Decision(IReadOnlyDictionary<string, string> answers, string key, int row) =>
        answers.TryGetValue(key + "_r" + row.ToString(CultureInfo.InvariantCulture), out var answer)
            ? answer switch { "true" => true, "false" => false, _ => null } : null;

    private static (string Name, int Row)? Suffix(string key)
    {
        var at = key.LastIndexOf("_r", StringComparison.Ordinal);
        return at > 0 && at + 2 < key.Length && !key.AsSpan(at + 2).ContainsAnyExceptInRange('0', '9')
            && int.TryParse(key.AsSpan(at + 2), NumberStyles.None, CultureInfo.InvariantCulture, out var row) ? (key[..at], row) : null;
    }
}
