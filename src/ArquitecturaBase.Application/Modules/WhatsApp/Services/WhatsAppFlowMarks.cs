using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Application.Resources;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// r14 (task 9b of the documents): how a form that reads documents opens with what was read. The compiler declares on the first screen
/// the data of the send: the notice atop it (<c>notice</c> and <c>notice_visible</c>), the help of each text or date control without its
/// own (<c>&lt;key&gt;_help</c>), the error-messages of each Form (<c>errors_&lt;screen&gt;</c>, like its init-values) and, for the question
/// that may receive the sent document on the last screen, the boolean that chooses its twin (<c>&lt;key&gt;_attached</c>). At dispatch the
/// sealed marks fill them: what was read with doubts or not read in the help of its control, what does not pass a fixed validation in the
/// error-messages and, for a control that cannot say it (a list, a yes or no, a number with its own help), a line of the notice.
/// </summary>
internal static class WhatsAppFlowMarks
{
    public const string Notice = "notice";
    public const string NoticeShown = "notice_visible";
    public static string Help(string key) => key + "_help";
    public static string Attached(string key) => key + "_attached";
    public static string Errors(string screenId) => "errors_" + screenId;
    public static string AttachedScreen(string screenId) => screenId + "_ATTACHED";

    // The question the inbox gives the document (FormReceivedDocumentSendPolicy): the first one of a document or the first one of a photo,
    // outside the block. Its twin needs a screen before it to choose, and is the last one so that each twin completes with its own form.
    // The server adds only the sent file, when the person chooses none: a question that needs two or more has no twin. A condition never
    // gets here, because the Flow does not take one on a photo or a document (WhatsAppFlowCompiler).
    public static int Twin(IReadOnlyList<StructuredFormQuestion> questions, IReadOnlyList<WhatsAppFlowScreen> screens)
    {
        var last = screens.Count - 1;
        if (last < 1 || screens[last].Kind != "fields" || screens[last].Questions.Count == 0) return -1;
        var first = screens[last].Questions[0];
        var question = questions[first];
        return question.Files is { MinFiles: <= 1 } && question.Repeated != true && question.Type is "photo" or "document"
            && Enumerable.Range(0, first).All(index => questions[index].Type != question.Type || questions[index].Files is null || questions[index].Repeated == true)
            ? last : -1;
    }

    public static void DeclareNotice(Dictionary<string, object> data, string culture)
    {
        data[Notice] = new { type = "string", __example__ = FlowTexts.Get("DocumentNotice", culture) };
        data[NoticeShown] = new { type = "boolean", __example__ = false };
    }

    public static Dictionary<string, object> NoticeText() => new(StringComparer.Ordinal)
    {
        ["type"] = "TextBody", ["text"] = "${data." + Notice + "}", ["visible"] = "${data." + NoticeShown + "}",
    };

    public static Dictionary<string, object> AttachedNotice(StructuredFormQuestion question, string culture) => new(StringComparer.Ordinal)
    {
        ["type"] = "TextBody", ["text"] = FlowTexts.Get(question.Type == "photo" ? "AttachedPhoto" : "AttachedDocument", culture),
    };

    // The twin's picker: the same question, optional, because the server adds the sent document when the person chooses none.
    public static Dictionary<string, object> Optional(Dictionary<string, object> control, StructuredFormQuestion question) =>
        new(control, StringComparer.Ordinal) { ["min-uploaded-" + (question.Type == "photo" ? "photos" : "documents")] = 0 };

    // A later screen reads the data of the first one.
    public static void FromFirstScreen(Dictionary<string, object> control)
    {
        if (control.TryGetValue("helper-text", out var help) && help is string text && text.StartsWith("${data.", StringComparison.Ordinal)
            && text.EndsWith("_help}", StringComparison.Ordinal))
            control["helper-text"] = "${screen.FORM.data." + text["${data.".Length..];
    }

    public static void DeclareErrors(Dictionary<string, object> data, Dictionary<string, object?> form, string screenId, bool first,
        IEnumerable<StructuredFormQuestion> scalars)
    {
        var key = Errors(screenId);
        data[key] = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = scalars.ToDictionary(question => question.Key, _ => new { type = "string" }, StringComparer.Ordinal),
            ["__example__"] = new Dictionary<string, string>(),
        };
        form["error-messages"] = (first ? "${data." : "${screen.FORM.data.") + key + "}";
    }

    /// <summary>
    /// The data of a send for the r14 data its Flow declares; nothing for another Flow. The notice goes only with what the server sealed
    /// (a received document or a load of a spreadsheet): a send of an operator opens as always.
    /// </summary>
    public static WhatsAppFlowDynamicData Build(WhatsAppFlowProjection projection, WhatsAppFlowServerData? server)
    {
        var declared = Declared(projection.Json);
        if (!declared.Contains(NoticeShown)) return WhatsAppFlowDynamicData.Empty;
        var culture = projection.Culture;
        var marks = (server?.Marks ?? []).GroupBy(mark => mark.FieldId).ToDictionary(group => group.Key, group => group.First());
        var objects = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        var lines = new List<string>();
        var shown = new HashSet<Guid>();
        var screens = WhatsAppFlowScreenBuilder.Plan(projection.Mapping.Select(field =>
            new WhatsAppFlowSlot(field.Type, field.Files is not null, field.Repeated == true, field.Rows, field.Calculated == true || field.Type == "location")).ToArray());
        foreach (var screen in screens.Where(screen => screen.Kind == "fields" && declared.Contains(Errors(screen.Id))))
        {
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in screen.Questions.Select(index => projection.Mapping[index]))
                if (marks.TryGetValue(field.FieldId, out var mark) && mark.Kind == StructuredDocumentMarkKind.Invalid && IsScalar(field))
                {
                    errors[field.Key] = Invalid(mark, culture);
                    shown.Add(field.FieldId);
                }
            if (errors.Count > 0) objects[Errors(screen.Id)] = errors;
        }
        foreach (var field in projection.Mapping)
        {
            marks.TryGetValue(field.FieldId, out var mark);
            if (declared.Contains(Help(field.Key)))
            {
                texts[Help(field.Key)] = mark?.Kind switch
                {
                    StructuredDocumentMarkKind.Doubtful => FlowTexts.Get("MarkDoubtful", culture),
                    StructuredDocumentMarkKind.NotRead => FlowTexts.Get("MarkNotRead", culture),
                    _ => "",
                };
                if (mark?.Kind is StructuredDocumentMarkKind.Doubtful or StructuredDocumentMarkKind.NotRead) shown.Add(field.FieldId);
            }
            if (declared.Contains(Attached(field.Key))) flags[Attached(field.Key)] = server?.File?.FieldId == field.FieldId;
            if (mark is null || shown.Contains(field.FieldId) || WhatsAppFlowMediaPolicy.QuestionLabel(projection, field.FieldId) is not { } label) continue;
            switch (mark.Kind)
            {
                case StructuredDocumentMarkKind.Doubtful: lines.Add(DisplayNumbers.Format(culture, FlowTexts.Get("NoticeDoubtful", culture), label)); break;
                case StructuredDocumentMarkKind.NotRead: lines.Add(DisplayNumbers.Format(culture, FlowTexts.Get("NoticeNotRead", culture), label)); break;
                case StructuredDocumentMarkKind.Invalid: lines.Add(DisplayNumbers.Format(culture, FlowTexts.Get("NoticeInvalid", culture), label, Invalid(mark, culture))); break;
            }
        }
        if (server is null)
        {
            texts[Notice] = "";
            flags[NoticeShown] = false;
        }
        else
        {
            // Task 9c: the form again after a fixed validation asks to correct what is marked, whether it came from a document or not.
            var head = FlowTexts.Get(server.Correction == true ? "CorrectionNotice" : server.Spreadsheet == true ? "SpreadsheetNotice" : "DocumentNotice", culture);
            texts[Notice] = string.Join("\n", [head, .. lines]);
            flags[NoticeShown] = true;
        }
        return new(objects, texts, flags);
    }

    // The text of the fixed validation a datum does not pass, in the send's language.
    public static string Invalid(StructuredDocumentFieldMark mark, string culture) =>
        mark.ErrorCode is { } code && ErrorMessages.ResourceManager.GetString(code, CultureInfo.GetCultureInfo(culture)) is { Length: > 0 } text
            ? text : FlowTexts.Get("MarkInvalid", culture);

    private static bool IsScalar(WhatsAppFlowProjectionField field) => field.Files is null && field.Type is not ("multiChoice" or "group" or "location")
        && field.Repeated != true && field.ChoosesFirst != true && field.Calculated != true && field.ShowWhen is null;

    /// <summary>The positions of the notices of a sealed screen (the r14 one atop the first screen, a twin's), which Meta's diagnostics count.</summary>
    public static int[] Notices(string json, int screenIndex)
    {
        if (!json.Contains("\"TextBody\"", StringComparison.Ordinal)) return [];
        using var document = JsonDocument.Parse(json);
        var screens = document.RootElement.GetProperty("screens");
        return screenIndex < 0 || screenIndex >= screens.GetArrayLength() ? [] : screens[screenIndex].GetProperty("layout").GetProperty("children")[0]
            .GetProperty("children").EnumerateArray().Select((child, at) => (Type: child.GetProperty("type").GetString(), At: at))
            .Where(child => child.Type == "TextBody").Select(child => child.At).ToArray();
    }

    private static HashSet<string> Declared(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var first = document.RootElement.GetProperty("screens").EnumerateArray().FirstOrDefault();
            return first.ValueKind == JsonValueKind.Object && first.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                ? data.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal) : [];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return [];
        }
    }
}

/// <summary>The r14 data of a send (task 9b): objects (the error-messages of each Form), texts (the notice and each help) and booleans.</summary>
internal sealed record WhatsAppFlowDynamicData(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Objects,
    IReadOnlyDictionary<string, string> Texts, IReadOnlyDictionary<string, bool> Flags)
{
    public static WhatsAppFlowDynamicData Empty { get; } = new(new Dictionary<string, IReadOnlyDictionary<string, string>>(),
        new Dictionary<string, string>(), new Dictionary<string, bool>());
    public override string ToString() => "WhatsAppFlowDynamicData { Content = [REDACTED] }";
}
