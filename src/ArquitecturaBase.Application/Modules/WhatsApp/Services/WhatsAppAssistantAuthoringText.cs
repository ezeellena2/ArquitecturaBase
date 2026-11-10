using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// IA3-A in fixed texts: the owner's summary is the preview, in the person's language, and a message with buttons holds 1024
// characters, so the questions that do not fit join the ones counted at the end. Nothing comes from the model's own words: a
// request that does not say what to build gets a fixed question and waits for the answer («pregunta y espera»). With a spreadsheet
// (IA, Stage 2), a catalog says how many rows it takes, an import is reviewed in the app and the question asks what to do with it.
internal static class WhatsAppAssistantAuthoringText
{
    private const int MaximumBody = 1024;

    public static WhatsAppAssistantTerminalResponse? Summary(Guid requestId, Guid proposalId, StructuredAuthoringSummary summary, string culture,
        Uri? publicOrigin, bool spreadsheet = false)
    {
        if (summary.Kind == StructuredAuthoringKind.Declined)
        {
            var declined = summary.Reason switch
            {
                "noPermission" => Text("AuthoringNoPermission", culture),
                "unclear" => Text(spreadsheet ? "AuthoringClarifySpreadsheet" : "AuthoringClarify", culture),
                "formExists" when Link(publicOrigin, summary.AppPath) is { } link => Text("AuthoringFormExists", culture, link),
                "catalogExists" when Link(publicOrigin, summary.AppPath) is { } link => Text("AuthoringCatalogExists", culture, link),
                _ => null,
            };
            return declined is null ? null : new(proposalId, declined, culture) { AwaitsAnswer = summary.Reason == "unclear" };
        }
        if (summary.Kind == StructuredAuthoringKind.Import)
            return new(proposalId, Sentence("AuthoringImport", summary, culture, Rows(summary.RowCount ?? 0, culture)), culture)
            {
                Buttons = [new(WhatsAppAssistantAuthoringReplyCodec.Encode(requestId, proposalId, WhatsAppAssistantAuthoringReplyCodec.Review),
                        Text("AuthoringReviewImport", culture), 0),
                    new(WhatsAppAssistantAuthoringReplyCodec.Encode(requestId, proposalId, WhatsAppAssistantAuthoringReplyCodec.Change), Text("AuthoringChange", culture), 0)],
            };
        var form = summary.Kind == StructuredAuthoringKind.Form;
        var body = form ? FormBody(summary, culture)
            : summary.RowCount is { } rows ? Sentence("AuthoringCatalogRows", summary, culture, Rows(rows, culture)) : Sentence("AuthoringCatalog", summary, culture);
        return new(proposalId, body, culture)
        {
            Buttons = [new(WhatsAppAssistantAuthoringReplyCodec.Encode(requestId, proposalId, WhatsAppAssistantAuthoringReplyCodec.Create),
                    Text(form ? "AuthoringCreateForm" : "AuthoringCreateCatalog", culture), 0),
                new(WhatsAppAssistantAuthoringReplyCodec.Encode(requestId, proposalId, WhatsAppAssistantAuthoringReplyCodec.Change), Text("AuthoringChange", culture), 0)],
        };
    }

    public static string Created(StructuredAuthoringCreatedResponse created, string culture, Uri publicOrigin) =>
        Text(created.Kind == StructuredAuthoringKind.Form ? "AuthoringFormCreated" : "AuthoringCatalogCreated", culture, created.Name, Link(publicOrigin, created.AppPath)!);

    /// <summary>The reply to «Revisar importación»: the link to that catalog's import in the app, which creates nothing.</summary>
    public static string? Review(StructuredAuthoringSummary? summary, string culture, Uri? publicOrigin) =>
        summary is { Kind: StructuredAuthoringKind.Import } && Link(publicOrigin, summary.AppPath) is { } link ? Text("AuthoringImportReview", culture, link) : null;

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

    // A catalog or an import is one sentence; a description that does not fit is cut where it stops fitting.
    private static string Sentence(string key, StructuredAuthoringSummary summary, string culture, params string[] after)
    {
        var line = summary.Lines.Count == 0 ? "" : summary.Lines[0];
        var body = Text(key, culture, [summary.Title, line, .. after]);
        if (body.Length <= MaximumBody) return body;
        var cut = Math.Max(0, line.Length - (body.Length - MaximumBody) - 1);
        return Text(key, culture, [summary.Title, line[..cut] + "…", .. after]);
    }

    // «Son 86 filas.», with the number as the person writes it.
    private static string Rows(int rows, string culture) => rows == 1 ? Text("AuthoringRowsOne", culture)
        : Text("AuthoringRowsMany", culture, rows.ToString("N0", CultureInfo.GetCultureInfo(culture)));

    private static string? Link(Uri? publicOrigin, string? appPath) =>
        publicOrigin is null || appPath is null ? null : new Uri(publicOrigin, appPath.TrimStart('/')).AbsoluteUri;
}
