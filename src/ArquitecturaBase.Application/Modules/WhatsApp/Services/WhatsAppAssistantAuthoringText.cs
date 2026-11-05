using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// IA3-A in fixed texts: the owner's summary is the preview, in the person's language, and a message with buttons holds 1024
// characters, so the questions that do not fit join the ones counted at the end. Nothing comes from the model's own words.
internal static class WhatsAppAssistantAuthoringText
{
    private const int MaximumBody = 1024;

    public static WhatsAppAssistantTerminalResponse? Summary(Guid requestId, Guid proposalId, StructuredAuthoringSummary summary, string culture, Uri? publicOrigin)
    {
        if (summary.Kind == StructuredAuthoringKind.Declined)
        {
            var declined = summary.Reason switch
            {
                "noPermission" => Text("AuthoringNoPermission", culture),
                "formExists" when Link(publicOrigin, summary.AppPath) is { } link => Text("AuthoringFormExists", culture, link),
                "catalogExists" when Link(publicOrigin, summary.AppPath) is { } link => Text("AuthoringCatalogExists", culture, link),
                _ => null,
            };
            return declined is null ? null : new(proposalId, declined, culture);
        }
        var form = summary.Kind == StructuredAuthoringKind.Form;
        var body = form ? FormBody(summary, culture) : CatalogBody(summary, culture);
        return new(proposalId, body, culture)
        {
            Buttons = [new(WhatsAppAssistantAuthoringReplyCodec.Encode(requestId, proposalId, WhatsAppAssistantAuthoringReplyCodec.Create),
                    Text(form ? "AuthoringCreateForm" : "AuthoringCreateCatalog", culture), 0),
                new(WhatsAppAssistantAuthoringReplyCodec.Encode(requestId, proposalId, WhatsAppAssistantAuthoringReplyCodec.Change), Text("AuthoringChange", culture), 0)],
        };
    }

    public static string Created(StructuredAuthoringCreatedResponse created, string culture, Uri publicOrigin) =>
        Text(created.Kind == StructuredAuthoringKind.Form ? "AuthoringFormCreated" : "AuthoringCatalogCreated", culture, created.Name, Link(publicOrigin, created.AppPath)!);

    public static string Text(string key, string culture, params object[] values) => values.Length == 0 ? AssistantTerminalTexts.Get(key, culture)
        : string.Format(CultureInfo.InvariantCulture, AssistantTerminalTexts.Get(key, culture), values);

    private static string FormBody(StructuredAuthoringSummary summary, string culture)
    {
        var lines = summary.Lines.ToList();
        var more = summary.MoreCount;
        var body = FormBody(summary.Title, lines, more, culture);
        while (body.Length > MaximumBody && lines.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
            body = FormBody(summary.Title, lines, ++more, culture);
        }
        return body;
    }

    private static string FormBody(string title, IReadOnlyList<string> lines, int more, string culture) => string.Join("\n",
        new[] { Text("AuthoringFormIntro", culture), "*" + title + "*" }.Concat(lines)
            .Concat(more == 0 ? [] : [more == 1 ? Text("AuthoringMoreOne", culture) : Text("AuthoringMoreMany", culture, more)])
            .Append(Text("AuthoringAsk", culture)));

    // A catalog is one sentence; a description that does not fit is cut where it stops fitting.
    private static string CatalogBody(StructuredAuthoringSummary summary, string culture)
    {
        var line = summary.Lines.Count == 0 ? "" : summary.Lines[0];
        var body = Text("AuthoringCatalog", culture, summary.Title, line);
        if (body.Length <= MaximumBody) return body;
        var cut = Math.Max(0, line.Length - (body.Length - MaximumBody) - 1);
        return Text("AuthoringCatalog", culture, summary.Title, line[..cut] + "…");
    }

    private static string? Link(Uri? publicOrigin, string? appPath) =>
        publicOrigin is null || appPath is null ? null : new Uri(publicOrigin, appPath.TrimStart('/')).AbsoluteUri;
}
