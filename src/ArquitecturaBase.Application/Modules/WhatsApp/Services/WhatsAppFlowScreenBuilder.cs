using System.Text.Json;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal static class WhatsAppFlowScreenBuilder
{
    internal static readonly string[] DocumentMimeTypes = ["application/pdf", "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "image/jpeg", "image/png"];

    public static bool ValidFiles(StructuredFormQuestion question) => question.Type is "photo" or "document"
        ? question.Files is { MinFiles: >= 0 and <= 10, MaxFiles: >= 1 and <= 10 } files
            && files.MinFiles <= files.MaxFiles && question.Required == (files.MinFiles > 0)
            && (question.Type == "photo" ? files.PhotoSource is "cameraOrGallery" or "camera" : files.PhotoSource is null)
        : question.Files is null;

    public static bool ValidSelection(StructuredFormQuestion question) => question.Type == "multiChoice"
        ? question.Selection is { MinSelected: >= 0 and <= 20, MaxSelected: >= 1 and <= 20 } selection
            && selection.MinSelected <= selection.MaxSelected && question.Required == (selection.MinSelected > 0)
            && (question.SourceCatalogId is not null || question.Options is { } options && selection.MinSelected <= options.Count)
        : question.Selection is null;

    // The block carries the rows and their minimum; its questions are only the simple ones (RG2-A).
    public static bool ValidRows(StructuredFormQuestion question) => question.Type == "group"
        ? question.Rows is { MinRows: >= 0 and <= 100, MaxRows: >= 1 and <= 100 } rows && rows.MinRows <= rows.MaxRows
            && question.Required == (rows.MinRows > 0) && question.Repeated != true
        : question.Rows is null
            && (question.Repeated != true || question.Type is "shortText" or "longText" or "decimal" or "date" or "boolean" or "choice");

    public static void ConfigurePicker(Dictionary<string, object> control, StructuredFormQuestion question, int maxFileSizeMiB)
    {
        control.Remove("required");
        var suffix = question.Type == "photo" ? "photos" : "documents";
        control["min-uploaded-" + suffix] = question.Files!.MinFiles;
        control["max-uploaded-" + suffix] = question.Files.MaxFiles;
        control["max-file-size-kb"] = maxFileSizeMiB * 1024;
        if (question.Type == "photo") control["photo-source"] = question.Files.PhotoSource == "camera" ? "camera" : "camera_gallery";
        else control["allowed-mime-types"] = DocumentMimeTypes;
    }

    // The options of a catalog question arrive as data: with the message, or with the update_data of a value (LD2-A).
    internal static object OptionsSchema() => new
    {
        type = "array",
        items = new
        {
            type = "object",
            properties = new
            {
                id = new { type = "string" },
                title = new { type = "string" },
                description = new { type = "string" },
            },
        },
        __example__ = Array.Empty<object>(),
    };

    // LD2-A (r11): a catalog question that chooses first by a field shows a dropdown of values before its own list, which waits
    // empty and hidden until the value chosen fills it with update_data. The values and their label come with the message to the
    // first screen, so the compiled Flow does not depend on the field's name. A required dropdown cannot be hidden («Required
    // Dropdown … cannot be invisible» when the Flow opens): the question is required while it shows.
    internal static void ChooseFirst(Dictionary<string, object> control, StructuredFormQuestion question, Dictionary<string, object> firstScreenData, string culture)
    {
        control["visible"] = "${data." + question.Key + "_visible}";
        if (question.Required) control["required"] = "${data." + question.Key + "_visible}";
        firstScreenData[question.Key + "_groups"] = new
        {
            type = "array",
            items = new { type = "object", properties = new { id = new { type = "string" }, title = new { type = "string" } } },
            __example__ = Array.Empty<object>(),
        };
        firstScreenData[question.Key + "_group_label"] = new { type = "string", __example__ = FlowTexts.Get("ChooseFirst", culture) };
        firstScreenData[question.Key + "_visible"] = VisibleSchema();
    }

    private static object VisibleSchema() => new { type = "boolean", __example__ = false };

    // The dropdown of values goes right before its question; on a later screen it reads the values from the first one.
    private static IEnumerable<Dictionary<string, object>> WithPicker(StructuredFormQuestion question, Dictionary<string, object> control, bool first)
    {
        if (question.ChoosesFirst != true) return [control];
        var data = first ? "${data." : "${screen.FORM.data.";
        return [new(StringComparer.Ordinal)
        {
            ["type"] = "Dropdown", ["name"] = question.Key + "_group", ["label"] = data + question.Key + "_group_label}",
            ["required"] = question.Required, ["data-source"] = data + question.Key + "_groups}",
        }, control];
    }

    // A screen after the first declares the list of each question that chooses first, and the navigate into it passes them
    // empty and hidden (Meta rejects a navigate without the data of its screen).
    private static Dictionary<string, object> Lists(IReadOnlyList<StructuredFormQuestion> questions, IEnumerable<int> indices) =>
        indices.Where(index => questions[index].ChoosesFirst == true).SelectMany(index => new KeyValuePair<string, object>[]
        {
            new(questions[index].Key + "_options", OptionsSchema()), new(questions[index].Key + "_visible", VisibleSchema()),
        }).ToDictionary(StringComparer.Ordinal);

    private static Dictionary<string, object> Entry(IReadOnlyList<StructuredFormQuestion> questions, IEnumerable<int> indices) =>
        indices.Where(index => questions[index].ChoosesFirst == true).SelectMany(index => new KeyValuePair<string, object>[]
        {
            new(questions[index].Key + "_options", Array.Empty<object>()), new(questions[index].Key + "_visible", false),
        }).ToDictionary(StringComparer.Ordinal);

    // CO1-A (r13): the conditions a question shows under, outermost first. Meta's validator takes one comparison but refuses «||»
    // and «&&» between two, and the same control in two branches (2026-11-11). One answer compares: ${form.<key>} on the screen of
    // its question, ${screen.<id>.form.<key>} on a later one. Several answers read the boolean <key>_shown, which each option of
    // its question sets with update_data. A chain nests the If of the question it depends on around its own.
    internal static IReadOnlyList<string> Conditions(IReadOnlyList<StructuredFormQuestion> questions, IReadOnlyList<WhatsAppFlowScreen> screens, int index, int at)
    {
        if (questions[index].ShowWhen is not { } showWhen) return [];
        var controller = Controller(questions, index);
        string own;
        if (Flagged(questions[index])) own = "${data." + Shown(questions[index]) + "}";
        else
        {
            var owner = Owner(screens, controller);
            var key = questions[controller].Key;
            own = (owner == at ? "${form." + key + "}" : "${screen." + screens[owner].Id + ".form." + key + "}") + " == '" + showWhen.Values[0] + "'";
        }
        return [.. Conditions(questions, screens, controller, at), own];
    }

    internal static int Controller(IReadOnlyList<StructuredFormQuestion> questions, int index) =>
        Enumerable.Range(0, index).First(earlier => questions[earlier].FieldId == questions[index].ShowWhen!.FieldId);

    // How many If a question goes inside: one per condition along its chain.
    internal static int Depth(IReadOnlyList<StructuredFormQuestion> questions, int index) =>
        questions[index].ShowWhen is null ? 0 : 1 + Depth(questions, Controller(questions, index));

    // A question shown by several answers of its question reads a boolean that the options of that question set.
    internal static bool Flagged(StructuredFormQuestion question) => question.ShowWhen is { Values.Count: > 1 };

    internal static string Shown(StructuredFormQuestion question) => question.Key + "_shown";

    // The question whose options set a boolean opens empty, so the boolean opens false: it is never prefilled.
    internal static bool SetsShown(IReadOnlyList<StructuredFormQuestion> questions, int index) =>
        questions.Any(question => Flagged(question) && question.ShowWhen!.FieldId == questions[index].FieldId);

    // The update_data of an option: for each question it shows by several answers, whether this option is one of them.
    internal static Dictionary<string, object>? OnSelect(IReadOnlyList<StructuredFormQuestion> questions, int index, string option)
    {
        var shown = questions.Where(question => Flagged(question) && question.ShowWhen!.FieldId == questions[index].FieldId)
            .ToDictionary(Shown, question => (object)question.ShowWhen!.Values.Contains(option, StringComparer.Ordinal), StringComparer.Ordinal);
        return shown.Count == 0 ? null : new(StringComparer.Ordinal) { ["name"] = "update_data", ["payload"] = shown };
    }

    // A title whose questions all share one condition (the same answers of the same question along the chain) shows with them. Meta
    // cannot hear «any of them», so a title over different conditions always shows.
    internal static IReadOnlyList<string>? Shared(IReadOnlyList<StructuredFormQuestion> questions, IReadOnlyList<WhatsAppFlowScreen> screens, IReadOnlyList<int> indices, int at) =>
        indices.Select(index => Signature(questions, index)).Distinct(StringComparer.Ordinal).Count() == 1 ? Conditions(questions, screens, indices[0], at) : null;

    private static string Signature(IReadOnlyList<StructuredFormQuestion> questions, int index) => questions[index].ShowWhen is not { } showWhen ? ""
        : Signature(questions, Controller(questions, index)) + "|" + showWhen.FieldId.ToString("N") + ":" + string.Join(",", showWhen.Values.Order(StringComparer.Ordinal));

    // Where each boolean lives: from the screen of the question whose options set it to the last screen that reads it, that of the
    // question it shows or of one further down its chain.
    internal static List<string>[] Flags(IReadOnlyList<StructuredFormQuestion> questions, IReadOnlyList<WhatsAppFlowScreen> screens)
    {
        var spans = new Dictionary<string, (int From, int To)>(StringComparer.Ordinal);
        for (var index = 0; index < questions.Count; index++)
        {
            if (questions[index].Calculated == true) continue;
            for (var link = index; questions[link].ShowWhen is not null; link = Controller(questions, link))
            {
                if (!Flagged(questions[link])) continue;
                var from = Owner(screens, Controller(questions, link));
                var to = Owner(screens, index);
                spans[Shown(questions[link])] = spans.TryGetValue(Shown(questions[link]), out var span) ? (from, Math.Max(span.To, to)) : (from, to);
            }
        }
        var flags = screens.Select(_ => new List<string>()).ToArray();
        foreach (var (flag, span) in spans.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            for (var at = span.From; at <= span.To; at++) flags[at].Add(flag);
        return flags;
    }

    // A navigate passes the booleans of the screen it goes to (Meta rejects a navigate without the data of its screen): what this
    // screen holds, or false where the question whose options set it is on that screen.
    private static Dictionary<string, object> Pass(Dictionary<string, object> payload, List<string>[] flags, int from, int to)
    {
        foreach (var flag in flags[to]) payload[flag] = flags[from].Contains(flag) ? "${data." + flag + "}" : false;
        return payload;
    }

    private static Dictionary<string, object> Declare(Dictionary<string, object> data, List<string> flags)
    {
        foreach (var flag in flags) data[flag] = VisibleSchema();
        return data;
    }

    // The screen that shows a question outside the block.
    internal static int Owner(IReadOnlyList<WhatsAppFlowScreen> screens, int index) =>
        Enumerable.Range(0, screens.Count).First(at => screens[at].Kind == "fields" && screens[at].Questions.Contains(index));

    // One If per condition, the outermost first; none has an else: hidden, the component is not on the screen.
    internal static Dictionary<string, object> Nest(IReadOnlyList<string> conditions, Dictionary<string, object> component) =>
        conditions.Reverse().Aggregate(component, (inner, condition) => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["type"] = "If", ["condition"] = condition, ["then"] = new[] { inner },
        });

    // Each component of a question with a condition goes inside its own If, so the components of a screen keep their places, which
    // the diagnostics of Meta count.
    private static IEnumerable<Dictionary<string, object>> Guarded(IReadOnlyList<StructuredFormQuestion> questions, IReadOnlyList<WhatsAppFlowScreen> screens,
        int index, int at, IEnumerable<Dictionary<string, object>> components) =>
        Conditions(questions, screens, index, at) is { Count: > 0 } conditions ? components.Select(component => Nest(conditions, component)) : components;

    // Without a block the screens are those of the plan: each file question starts one, and a calculation is on none (r13). r14 (task 9b)
    // adds, for a form that reads documents, the notice atop the first screen, the error-messages of each Form and the twin of the last
    // screen when it starts with the question that may receive the sent document: the screen before chooses one by a datum of the send.
    public static string Build(IReadOnlyList<StructuredFormQuestion> questions, IReadOnlyList<WhatsAppFlowScreen> plan, List<Dictionary<string, object>> controls,
        Dictionary<string, object> initialData, string? title, string? submit, string culture, IReadOnlyDictionary<int, Dictionary<string, object>> subtitles,
        bool reads = false, int twin = -1)
    {
        var groups = plan.Select(screen => screen.Questions).ToArray();
        var owners = groups.SelectMany((group, screen) => group.Select(index => (index, screen))).ToDictionary(value => value.index, value => value.screen);
        var flags = Flags(questions, plan);
        Declare(initialData, flags[0]);
        if (reads) WhatsAppFlowMarks.DeclareNotice(initialData, culture);
        var twinKey = twin > 0 ? questions[groups[twin][0]].Key : null;
        if (twinKey is not null) initialData[WhatsAppFlowMarks.Attached(twinKey)] = VisibleSchema();
        var screens = new List<Dictionary<string, object?>>();
        for (var screen = 0; screen < groups.Length; screen++) screens.Add(Screen(screen, false));
        if (twinKey is null)
        {
            if (initialData.Count > 0) screens[0]["data"] = initialData;
            return JsonSerializer.Serialize(new { version = "7.3", screens });
        }
        screens.Add(Screen(twin, true));
        if (initialData.Count > 0) screens[0]["data"] = initialData;
        // An If with a navigate in each branch: Meta deduces only one of them (2026-11-06), so the routing model names both.
        var routing = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var screen = 0; screen < groups.Length; screen++)
            routing[ScreenId(screen)] = screen == twin - 1 ? [ScreenId(twin), WhatsAppFlowMarks.AttachedScreen(ScreenId(twin))]
                : screen == groups.Length - 1 ? [] : [ScreenId(screen + 1)];
        routing[WhatsAppFlowMarks.AttachedScreen(ScreenId(twin))] = [];
        return JsonSerializer.Serialize(new { version = "7.3", routing_model = routing, screens });

        Dictionary<string, object?> Screen(int screen, bool attached)
        {
            var current = screen;
            var last = screen == groups.Length - 1;
            if (screen > 0)
            {
                foreach (var index in groups[screen].Where(index => questions[index].SourceCatalogId.HasValue && questions[index].ChoosesFirst != true))
                    controls[index]["data-source"] = "${screen.FORM.data." + questions[index].Key + "_options}";
                if (reads) foreach (var index in groups[screen]) WhatsAppFlowMarks.FromFirstScreen(controls[index]);
            }
            var children = groups[screen].SelectMany(index => WithSubtitle(index,
                Guarded(questions, plan, index, current, WithPicker(questions[index], attached && index == groups[screen][0]
                    ? WhatsAppFlowMarks.Optional(controls[index], questions[index]) : controls[index], current == 0)), subtitles)).ToList();
            if (attached) children.Insert(0, WhatsAppFlowMarks.AttachedNotice(questions[groups[screen][0]], culture));
            if (screen == 0 && reads) children.Insert(0, WhatsAppFlowMarks.NoticeText());
            object Next(string next) => new { name = "navigate", next = new { type = "screen", name = next }, payload = Pass(Entry(questions, groups[screen + 1]), flags, screen, screen + 1) };
            object action = last
                ? new { name = "complete", payload = questions.Select((question, index) => (question, index)).Where(value => owners.ContainsKey(value.index))
                    .Select(value => new { value.question.Key, Value = owners[value.index] == current ? "${form." + value.question.Key + "}"
                        : "${screen." + ScreenId(owners[value.index]) + ".form." + value.question.Key + "}" }).ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal) }
                : Next(ScreenId(screen + 1));
            var label = last ? submit ?? "" : FlowTexts.Get("Continue", culture);
            children.Add(twinKey is not null && screen == twin - 1
                ? new() { ["type"] = "If", ["condition"] = (screen == 0 ? "${data." : "${screen.FORM.data.") + WhatsAppFlowMarks.Attached(twinKey) + "}",
                    ["then"] = new[] { Footer(label, Next(WhatsAppFlowMarks.AttachedScreen(ScreenId(twin)))) }, ["else"] = new[] { Footer(label, action) } }
                : Footer(label, action));
            var form = new Dictionary<string, object?> { ["type"] = "Form", ["name"] = "form", ["children"] = children };
            // The list of a question that chooses first is empty until a value is chosen: it is never prefilled. Nor a question with
            // a condition (CO1-A), which may be hidden, or one whose options set a boolean.
            var scalarQuestions = groups[screen].Where(index => questions[index].Files is null && questions[index].Type != "multiChoice"
                && questions[index].ChoosesFirst != true && questions[index].ShowWhen is null && !SetsShown(questions, index)).Select(index => questions[index]).ToArray();
            if (scalarQuestions.Length > 0)
            {
                var key = WhatsAppFlowPrefillPolicy.DataKey(ScreenId(screen));
                initialData[key] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = scalarQuestions.ToDictionary(question => question.Key, _ => new { type = "string" }, StringComparer.Ordinal),
                    ["__example__"] = new Dictionary<string, string>(),
                };
                form["init-values"] = screen == 0 ? "$" + "{data." + key + "}" : "$" + "{screen.FORM.data." + key + "}";
                if (reads) WhatsAppFlowMarks.DeclareErrors(initialData, form, ScreenId(screen), screen == 0, scalarQuestions);
            }
            var item = new Dictionary<string, object?> { ["id"] = attached ? WhatsAppFlowMarks.AttachedScreen(ScreenId(screen)) : ScreenId(screen), ["title"] = title,
                ["layout"] = new { type = "SingleColumnLayout", children = new[] { form } } };
            if (last) item["terminal"] = true;
            if (screen > 0 && Declare(Lists(questions, groups[screen]), flags[screen]) is { Count: > 0 } lists) item["data"] = lists;
            return item;
        }

        static Dictionary<string, object> Footer(string label, object action) => new(StringComparer.Ordinal)
        {
            ["type"] = "Footer", ["label"] = label, ["on-click-action"] = action,
        };
    }

    internal static List<List<int>> Groups(IReadOnlyList<WhatsAppFlowSlot> slots)
    {
        List<List<int>> groups = [[]];
        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index].Calculated) continue;
            if (slots[index].Files && groups[^1].Count > 0) groups.Add([]);
            groups[^1].Add(index);
        }
        return groups;
    }

    internal static string ScreenId(int index) => index == 0 ? "FORM" : "FORM_" + (char)('A' + index - 1);

    // Screens in question order. Without a block, each file question starts a screen, as r9. With one: what comes before it,
    // the question of a minimum of zero (RG8-A), a screen per row up to the channel limit (RG3-A) and what comes after. With
    // nothing after the block, a closing screen ends it (r12): Meta does not let a screen that may go on to another row complete.
    // A calculation is on no screen (r13), so with only calculations after the block the closing screen ends it too.
    internal static IReadOnlyList<WhatsAppFlowScreen> Plan(IReadOnlyList<WhatsAppFlowSlot> slots)
    {
        var screens = new List<(string Kind, List<int> Questions, int Row)>();
        var block = Enumerable.Range(0, slots.Count).FirstOrDefault(index => slots[index].Type == "group", -1);
        if (block < 0)
            foreach (var group in Groups(slots)) screens.Add(("fields", group, 0));
        else
        {
            var end = block + 1;
            while (end < slots.Count && slots[end].Repeated) end++;
            AddFields(0, block);
            var rows = slots[block].Rows!;
            if (rows.MinRows == 0) screens.Add(("ask", [block], 0));
            var children = Enumerable.Range(block + 1, end - block - 1).Where(index => !slots[index].Calculated).ToList();
            for (var row = 1; row <= RowCount(rows); row++) screens.Add(("row", children, row));
            var before = screens.Count;
            AddFields(end, slots.Count);
            if (screens.Count == before) screens.Add(("end", [], 0));
        }
        return screens.Select((screen, index) => new WhatsAppFlowScreen(ScreenId(index), screen.Kind, screen.Questions.AsReadOnly(), screen.Row)).ToArray();

        void AddFields(int from, int to)
        {
            List<int>? current = null;
            for (var index = from; index < to; index++)
            {
                if (slots[index].Calculated) continue;
                if (current is null || (slots[index].Files && current.Count > 0)) { current = []; screens.Add(("fields", current, 0)); }
                current.Add(index);
            }
        }
    }

    // A section title goes right before its first question; a section that starts with the block, atop the block's first screen.
    private static IEnumerable<Dictionary<string, object>> WithSubtitle(int index, IEnumerable<Dictionary<string, object>> components,
        IReadOnlyDictionary<int, Dictionary<string, object>> subtitles) => subtitles.TryGetValue(index, out var subtitle) ? components.Prepend(subtitle) : components;

    internal static int Subtitled(WhatsAppFlowScreen screen, int block, StructuredFormRowSettings rows, IReadOnlyDictionary<int, Dictionary<string, object>> subtitles) =>
        screen.Kind == "fields" ? screen.Questions.Count(subtitles.ContainsKey)
            : subtitles.ContainsKey(block) && (screen.Kind == "ask" || (rows.MinRows > 0 && screen.Row == 1)) ? 1 : 0;

    internal static int RowCount(StructuredFormRowSettings rows) => Math.Min(Configuration.WhatsAppFlowProfile.MaxRows, rows.MaxRows);

    // From the minimum, a row ends with «¿Agregás otro ítem?»; the last one asks nothing.
    internal static bool Asks(StructuredFormRowSettings rows, int row) => row >= Math.Max(rows.MinRows, 1) && row < RowCount(rows);

    public static string BuildRows(IReadOnlyList<StructuredFormQuestion> questions, IReadOnlyList<WhatsAppFlowScreen> screens,
        List<Dictionary<string, object>> controls, Dictionary<string, object> initialData, string? title, string? blockTitle, string? submit, string culture,
        IReadOnlyDictionary<int, Dictionary<string, object>> subtitles, bool reads = false)
    {
        var block = questions.Select((question, index) => (question, index)).First(value => value.question.Type == "group");
        var rows = block.question.Rows!;
        // What follows the block: its next questions or, without them, the closing screen (r12).
        var after = screens.First(screen => screen.Kind == "end" || (screen.Kind == "fields" && screen.Questions[0] > block.index));
        var built = new List<Dictionary<string, object?>>();
        var routing = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var flags = Flags(questions, screens);
        Declare(initialData, flags[0]);
        if (reads) WhatsAppFlowMarks.DeclareNotice(initialData, culture);
        for (var current = 0; current < screens.Count; current++)
        {
            var screen = screens[current];
            var terminal = false;
            var routes = new List<string>();
            var children = new List<Dictionary<string, object>>();
            var form = new Dictionary<string, object?> { ["type"] = "Form", ["name"] = "form", ["children"] = children };
            if (current == 0 && reads) children.Add(WhatsAppFlowMarks.NoticeText());
            if (screen.Kind != "fields" && Subtitled(screen, block.index, rows, subtitles) > 0) children.Add(subtitles[block.index]);
            if (screen.Kind == "fields")
            {
                children.AddRange(screen.Questions.SelectMany(index => WithSubtitle(index,
                    Guarded(questions, screens, index, current, WithPicker(questions[index], Control(index, current), current == 0)), subtitles)));
                children.Add(current == screens.Count - 1 ? Footer(submit, Complete()) : Footer(FlowTexts.Get("Continue", culture), To(screens[current + 1])));
                var scalars = screen.Questions.Where(index => questions[index].Files is null && questions[index].Type != "multiChoice"
                    && questions[index].ChoosesFirst != true && questions[index].ShowWhen is null && !SetsShown(questions, index)).Select(index => questions[index]).ToArray();
                if (scalars.Length > 0)
                {
                    var key = WhatsAppFlowPrefillPolicy.DataKey(screen.Id);
                    initialData[key] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = scalars.ToDictionary(question => question.Key, _ => new { type = "string" }, StringComparer.Ordinal),
                        ["__example__"] = new Dictionary<string, string>(),
                    };
                    form["init-values"] = current == 0 ? "$" + "{data." + key + "}" : "$" + "{screen.FORM.data." + key + "}";
                    if (reads) WhatsAppFlowMarks.DeclareErrors(initialData, form, screen.Id, current == 0, scalars);
                }
            }
            else if (screen.Kind == "ask")
            {
                children.Add(Question("AddRow"));
                children.Add(Branch(screens[current + 1]));
            }
            else if (screen.Kind == "end")
            {
                children.Add(new(StringComparer.Ordinal)
                {
                    ["type"] = "TextBody", ["text"] = DisplayNumbers.Format(culture, FlowTexts.Get("Closing", culture), submit),
                });
                children.Add(Footer(submit, Complete()));
            }
            else
            {
                children.AddRange(screen.Questions.SelectMany(index => WithPicker(questions[index], Control(index, current), current == 0)));
                if (Asks(rows, screen.Row))
                {
                    children.Add(Question("AddAnotherRow"));
                    children.Add(Branch(screens[current + 1]));
                }
                else if (screen.Row < RowCount(rows)) children.Add(Footer(FlowTexts.Get("Continue", culture), To(screens[current + 1])));
                else children.Add(Exit());
            }
            var item = new Dictionary<string, object?>
            {
                ["id"] = screen.Id,
                ["title"] = screen.Kind is "fields" or "end" ? title : screen.Kind == "ask" ? blockTitle
                    : DisplayNumbers.Format(culture, FlowTexts.Get("RowTitle", culture), blockTitle, screen.Row),
                ["layout"] = new { type = "SingleColumnLayout", children = new[] { form } },
            };
            if (terminal) item["terminal"] = true;
            if (current > 0 && Declare(Lists(questions, screen.Questions), flags[current]) is { Count: > 0 } lists) item["data"] = lists;
            built.Add(item);
            routing[screen.Id] = [.. routes];

            Dictionary<string, object> Control(int index, int at)
            {
                var control = new Dictionary<string, object>(controls[index], StringComparer.Ordinal);
                if (at > 0 && questions[index].SourceCatalogId.HasValue && questions[index].ChoosesFirst != true)
                    control["data-source"] = "${screen.FORM.data." + questions[index].Key + "_options}";
                if (at > 0 && reads) WhatsAppFlowMarks.FromFirstScreen(control);
                return control;
            }
            Dictionary<string, object> Question(string text) => new(StringComparer.Ordinal)
            {
                ["type"] = "RadioButtonsGroup", ["name"] = block.question.Key, ["label"] = FlowTexts.Get(text, culture), ["required"] = true,
                ["data-source"] = new[] { new { id = "true", title = FlowTexts.Get("Yes", culture) }, new { id = "false", title = FlowTexts.Get("No", culture) } },
            };
            Dictionary<string, object> Branch(WhatsAppFlowScreen next) => new(StringComparer.Ordinal)
            {
                ["type"] = "If", ["condition"] = "${form." + block.question.Key + "} == 'true'",
                ["then"] = new[] { Footer(FlowTexts.Get("Continue", culture), To(next)) }, ["else"] = new[] { Exit() },
            };
            Dictionary<string, object> Exit() => Footer(FlowTexts.Get("Continue", culture), To(after));
            // Meta deduces the transitions from the navigate actions and leaves out the else branch of an If (2026-11-06):
            // every navigate goes into the routing model, both branches included.
            object To(WhatsAppFlowScreen next)
            {
                routes.Add(next.Id);
                return Navigate(next.Id, Pass(Entry(questions, next.Questions), flags, current, Enumerable.Range(0, screens.Count).First(at => screens[at].Id == next.Id)));
            }
            object Complete()
            {
                terminal = true;
                return new { name = "complete", payload = Payload() };
            }
            // Everything that may have been loaded up to this screen; a row's answers go under field_<question>_r<n>.
            Dictionary<string, string> Payload()
            {
                var payload = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var visited = 0; visited <= current; visited++)
                {
                    var at = screens[visited];
                    if (at.Kind == "fields") foreach (var index in at.Questions) payload[questions[index].Key] = Reference(visited, questions[index].Key);
                    else if (at.Kind == "ask") payload[block.question.Key + "_r0"] = Reference(visited, block.question.Key);
                    else if (at.Kind == "row")
                    {
                        foreach (var index in at.Questions) payload[questions[index].Key + "_r" + at.Row] = Reference(visited, questions[index].Key);
                        if (Asks(rows, at.Row)) payload[block.question.Key + "_r" + at.Row] = Reference(visited, block.question.Key);
                    }
                }
                return payload;
            }
            string Reference(int visited, string name) => visited == current ? "${form." + name + "}" : "${screen." + screens[visited].Id + ".form." + name + "}";
        }
        if (initialData.Count > 0) built[0]["data"] = initialData;
        return JsonSerializer.Serialize(new { version = "7.3", routing_model = routing, screens = built });

        static Dictionary<string, object> Footer(string? label, object action) => new(StringComparer.Ordinal)
        {
            ["type"] = "Footer", ["label"] = label ?? "", ["on-click-action"] = action,
        };
        static object Navigate(string next, Dictionary<string, object> payload) => new { name = "navigate", next = new { type = "screen", name = next }, payload };
    }
}

internal readonly record struct WhatsAppFlowSlot(string Type, bool Files, bool Repeated, StructuredFormRowSettings? Rows, bool Calculated = false);

internal sealed record WhatsAppFlowScreen(string Id, string Kind, IReadOnlyList<int> Questions, int Row);
