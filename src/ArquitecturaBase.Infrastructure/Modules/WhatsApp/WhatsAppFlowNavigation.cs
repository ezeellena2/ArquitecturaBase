using System.Globalization;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// Where a Flow starts and the data of its first screen: the options of its catalog lists and the values it opens with. A message
/// that opens a form sends it as <c>flow_action_payload</c>, and so does the interactive official preview (VO1-A).
/// </summary>
internal static class WhatsAppFlowNavigation
{
    public static JsonObject Build(string screen, IReadOnlyList<StructuredFormCatalogOffer> catalogs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> initialValues,
        IReadOnlyDictionary<string, string>? texts = null, IReadOnlyDictionary<string, bool>? flags = null)
    {
        var navigation = new JsonObject { ["screen"] = screen };
        if (catalogs.Count == 0 && initialValues.Count == 0 && (texts?.Count ?? 0) == 0 && (flags?.Count ?? 0) == 0) return navigation;
        var data = new JsonObject();
        foreach (var catalog in catalogs)
        {
            if (catalog.Grouping is { } grouping) Grouped(data, catalog, grouping);
            else data[catalog.Key + "_options"] = Options(catalog.Options);
        }
        foreach (var initial in initialValues)
        {
            var fields = new JsonObject();
            foreach (var field in initial.Value) fields[field.Key] = field.Value;
            data[initial.Key] = fields;
        }
        // r14 (task 9b): the notice, the help of each control and the booleans of the first screen.
        foreach (var text in texts ?? new Dictionary<string, string>()) data[text.Key] = text.Value;
        foreach (var flag in flags ?? new Dictionary<string, bool>()) data[flag.Key] = flag.Value;
        navigation["data"] = data;
        return navigation;
    }

    private static JsonArray Options(IEnumerable<StructuredFormCatalogOption> options) => new([.. options.Select(option =>
    {
        var item = new JsonObject { ["id"] = option.Id.ToString("D"), ["title"] = option.Shown?.Title ?? option.Text };
        var description = option.Shown is null ? option.Detail : option.Shown.Description;
        if (!string.IsNullOrEmpty(description)) item["description"] = description;
        return (JsonNode)item;
    })]);

    // LD2-A (r11): a question that chooses first by a field gets its values, each with the update_data that fills and shows the
    // list of its options, and that list empty and hidden. A value travels with an id of the message (g1, g2…): what is chosen in
    // the first dropdown is not part of the answer. The field's name and the values fit a dropdown's label and option titles.
    private static void Grouped(JsonObject data, StructuredFormCatalogOffer catalog, StructuredFormCatalogGrouping grouping)
    {
        var options = catalog.Options.ToLookup(option => option.Group);
        data[catalog.Key + "_groups"] = new JsonArray([.. grouping.Groups.Select((group, index) => (JsonNode)new JsonObject
        {
            ["id"] = "g" + (index + 1).ToString(CultureInfo.InvariantCulture),
            ["title"] = Fit(group.Text, GroupTitleLimit),
            ["on-select-action"] = new JsonObject
            {
                ["name"] = "update_data",
                ["payload"] = new JsonObject
                {
                    [catalog.Key + "_options"] = Options(options[group.Value]),
                    [catalog.Key + "_visible"] = true,
                },
            },
        })]);
        data[catalog.Key + "_group_label"] = Fit(grouping.Label, GroupLabelLimit);
        data[catalog.Key + "_options"] = new JsonArray();
        data[catalog.Key + "_visible"] = false;
    }

    private static string Fit(string text, int limit)
    {
        if (text.Length <= limit) return text;
        var cut = limit - 1;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd() + "…";
    }

    /// <summary>
    /// Meta's official preview link, running interactively from <paramref name="screen"/> with the options of <paramref name="catalogs"/>: their data travels in the link, so each list
    /// keeps fewer of its first options when the link would grow past <see cref="PreviewPayloadLimit"/> characters.
    /// </summary>
    public static string InteractivePreview(string url, string screen, IReadOnlyList<StructuredFormCatalogOffer> catalogs)
    {
        var escaped = "";
        foreach (var limit in PreviewOptionSteps)
        {
            var limited = catalogs.Select(catalog => Limit(catalog, limit)).ToArray();
            // Meta decodes the parameter twice (the browser and then its own decodeURIComponent): a literal % in an option, like
            // «2 4 D 100%», breaks the second one. The JSON escape % reads the same decoded once or twice; % only appears
            // inside JSON strings and is never part of another escape.
            escaped = Uri.EscapeDataString(Build(screen, limited, Empty).ToJsonString().Replace("%", "\\u0025", StringComparison.Ordinal));
            if (escaped.Length <= PreviewPayloadLimit) break;
        }
        return url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "interactive=true&flow_action=navigate&flow_action_payload=" + escaped;
    }

    // A grouped list keeps its first values, each with its first options.
    private static StructuredFormCatalogOffer Limit(StructuredFormCatalogOffer catalog, int limit)
    {
        if (catalog.Grouping is not { } grouping) return catalog with { Options = catalog.Options.Take(limit).ToArray() };
        var kept = grouping.Groups.Take(limit).ToArray();
        var options = catalog.Options.ToLookup(option => option.Group);
        return catalog with { Options = kept.SelectMany(group => options[group.Value].Take(limit)).ToArray(), Grouping = grouping with { Groups = kept } };
    }

    internal const int PreviewPayloadLimit = 6000;
    private const int GroupLabelLimit = 20;
    private const int GroupTitleLimit = 30;
    private static readonly int[] PreviewOptionSteps = [20, 10, 5, 3, 1, 0];
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Empty = new Dictionary<string, IReadOnlyDictionary<string, string>>();
}
