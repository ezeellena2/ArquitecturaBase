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
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> initialValues)
    {
        var navigation = new JsonObject { ["screen"] = screen };
        if (catalogs.Count == 0 && initialValues.Count == 0) return navigation;
        var data = new JsonObject();
        foreach (var catalog in catalogs)
            data[catalog.Key + "_options"] = new JsonArray([.. catalog.Options.Select(option =>
            {
                var item = new JsonObject { ["id"] = option.Id.ToString("D"), ["title"] = option.Shown?.Title ?? option.Text };
                var description = option.Shown is null ? option.Detail : option.Shown.Description;
                if (!string.IsNullOrEmpty(description)) item["description"] = description;
                return (JsonNode)item;
            })]);
        foreach (var initial in initialValues)
        {
            var fields = new JsonObject();
            foreach (var field in initial.Value) fields[field.Key] = field.Value;
            data[initial.Key] = fields;
        }
        navigation["data"] = data;
        return navigation;
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
            var limited = catalogs.Select(catalog => catalog with { Options = catalog.Options.Take(limit).ToArray() }).ToArray();
            escaped = Uri.EscapeDataString(Build(screen, limited, Empty).ToJsonString());
            if (escaped.Length <= PreviewPayloadLimit) break;
        }
        return url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "interactive=true&flow_action=navigate&flow_action_payload=" + escaped;
    }

    internal const int PreviewPayloadLimit = 6000;
    private static readonly int[] PreviewOptionSteps = [20, 10, 5, 3, 1, 0];
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Empty = new Dictionary<string, IReadOnlyDictionary<string, string>>();
}
