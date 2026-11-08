using System.Globalization;
using System.Text.Json;
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

    public static string Build(IReadOnlyList<StructuredFormQuestion> questions, List<Dictionary<string, object>> controls,
        Dictionary<string, object> initialData, string? title, string? submit, string culture, IReadOnlyDictionary<int, Dictionary<string, object>> subtitles)
    {
        var groups = Groups(questions.Select(question => question.Files is not null).ToArray());
        var owners = groups.SelectMany((group, screen) => group.Select(index => (index, screen))).ToDictionary(value => value.index, value => value.screen);
        var screens = new List<Dictionary<string, object?>>();
        for (var screen = 0; screen < groups.Count; screen++)
        {
            var current = screen;
            var last = screen == groups.Count - 1;
            if (screen > 0)
                foreach (var index in groups[screen].Where(index => questions[index].SourceCatalogId.HasValue && questions[index].ChoosesFirst != true))
                    controls[index]["data-source"] = "${screen.FORM.data." + questions[index].Key + "_options}";
            var children = groups[screen].SelectMany(index => WithSubtitle(index, WithPicker(questions[index], controls[index], current == 0), subtitles)).ToList();
            object action = last
                ? new { name = "complete", payload = questions.Select((question, index) => new { question.Key, Value = owners[index] == current
                    ? "${form." + question.Key + "}" : "${screen." + ScreenId(owners[index]) + ".form." + question.Key + "}" }).ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal) }
                : new { name = "navigate", next = new { type = "screen", name = ScreenId(screen + 1) }, payload = Entry(questions, groups[screen + 1]) };
            children.Add(new() { ["type"] = "Footer", ["label"] = last ? submit ?? "" : FlowTexts.Get("Continue", culture), ["on-click-action"] = action });
            var form = new Dictionary<string, object?> { ["type"] = "Form", ["name"] = "form", ["children"] = children };
            // The list of a question that chooses first is empty until a value is chosen: it is never prefilled.
            var scalarQuestions = groups[screen].Where(index => questions[index].Files is null && questions[index].Type != "multiChoice"
                && questions[index].ChoosesFirst != true).Select(index => questions[index]).ToArray();
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
            }
            var item = new Dictionary<string, object?> { ["id"] = ScreenId(screen), ["title"] = title,
                ["layout"] = new { type = "SingleColumnLayout", children = new[] { form } } };
            if (last) item["terminal"] = true;
            if (screen == 0 && initialData.Count > 0) item["data"] = initialData;
            else if (screen > 0 && Lists(questions, groups[screen]) is { Count: > 0 } lists) item["data"] = lists;
            screens.Add(item);
        }
        if (initialData.Count > 0) screens[0]["data"] = initialData;
        return JsonSerializer.Serialize(new { version = "7.3", screens });
    }

    internal static List<List<int>> Groups(IReadOnlyList<bool> startsScreen)
    {
        List<List<int>> groups = [[]];
        for (var index = 0; index < startsScreen.Count; index++)
        {
            if (startsScreen[index] && groups[^1].Count > 0) groups.Add([]);
            groups[^1].Add(index);
        }
        return groups;
    }

    internal static string ScreenId(int index) => index == 0 ? "FORM" : "FORM_" + (char)('A' + index - 1);

    // Screens in question order. Without a block, each file question starts a screen, as r9. With one: what comes before it,
    // the question of a minimum of zero (RG8-A), a screen per row up to the channel limit (RG3-A) and what comes after.
    internal static IReadOnlyList<WhatsAppFlowScreen> Plan(IReadOnlyList<WhatsAppFlowSlot> slots)
    {
        var screens = new List<(string Kind, List<int> Questions, int Row)>();
        var block = Enumerable.Range(0, slots.Count).FirstOrDefault(index => slots[index].Type == "group", -1);
        if (block < 0)
            foreach (var group in Groups(slots.Select(slot => slot.Files).ToArray())) screens.Add(("fields", group, 0));
        else
        {
            var end = block + 1;
            while (end < slots.Count && slots[end].Repeated) end++;
            AddFields(0, block);
            var rows = slots[block].Rows!;
            if (rows.MinRows == 0) screens.Add(("ask", [block], 0));
            var children = Enumerable.Range(block + 1, end - block - 1).ToList();
            for (var row = 1; row <= RowCount(rows); row++) screens.Add(("row", children, row));
            AddFields(end, slots.Count);
        }
        return screens.Select((screen, index) => new WhatsAppFlowScreen(ScreenId(index), screen.Kind, screen.Questions.AsReadOnly(), screen.Row)).ToArray();

        void AddFields(int from, int to)
        {
            List<int>? current = null;
            for (var index = from; index < to; index++)
            {
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
        IReadOnlyDictionary<int, Dictionary<string, object>> subtitles)
    {
        var block = questions.Select((question, index) => (question, index)).First(value => value.question.Type == "group");
        var rows = block.question.Rows!;
        var after = screens.FirstOrDefault(screen => screen.Kind == "fields" && screen.Questions[0] > block.index);
        var built = new List<Dictionary<string, object?>>();
        var routing = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var current = 0; current < screens.Count; current++)
        {
            var screen = screens[current];
            var terminal = false;
            var routes = new List<string>();
            var children = new List<Dictionary<string, object>>();
            var form = new Dictionary<string, object?> { ["type"] = "Form", ["name"] = "form", ["children"] = children };
            if (screen.Kind != "fields" && Subtitled(screen, block.index, rows, subtitles) > 0) children.Add(subtitles[block.index]);
            if (screen.Kind == "fields")
            {
                children.AddRange(screen.Questions.SelectMany(index => WithSubtitle(index, WithPicker(questions[index], Control(index, current), current == 0), subtitles)));
                children.Add(current == screens.Count - 1 ? Footer(submit, Complete()) : Footer(FlowTexts.Get("Continue", culture), To(screens[current + 1])));
                var scalars = screen.Questions.Where(index => questions[index].Files is null && questions[index].Type != "multiChoice"
                    && questions[index].ChoosesFirst != true).Select(index => questions[index]).ToArray();
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
                }
            }
            else if (screen.Kind == "ask")
            {
                children.Add(Question("AddRow"));
                children.Add(Branch(screens[current + 1]));
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
                ["title"] = screen.Kind == "fields" ? title : screen.Kind == "ask" ? blockTitle
                    : string.Format(CultureInfo.GetCultureInfo(culture), FlowTexts.Get("RowTitle", culture), blockTitle, screen.Row),
                ["layout"] = new { type = "SingleColumnLayout", children = new[] { form } },
            };
            if (terminal) item["terminal"] = true;
            if (current > 0 && Lists(questions, screen.Questions) is { Count: > 0 } lists) item["data"] = lists;
            built.Add(item);
            routing[screen.Id] = [.. routes];

            Dictionary<string, object> Control(int index, int at)
            {
                var control = new Dictionary<string, object>(controls[index], StringComparer.Ordinal);
                if (at > 0 && questions[index].SourceCatalogId.HasValue && questions[index].ChoosesFirst != true)
                    control["data-source"] = "${screen.FORM.data." + questions[index].Key + "_options}";
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
            Dictionary<string, object> Exit() => after is null ? Footer(submit, Complete()) : Footer(FlowTexts.Get("Continue", culture), To(after));
            // Meta deduces the transitions from the navigate actions and leaves out the else branch of an If (2026-11-06):
            // every navigate goes into the routing model, both branches included.
            object To(WhatsAppFlowScreen next)
            {
                routes.Add(next.Id);
                return Navigate(next.Id, Entry(questions, next.Questions));
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
                    else
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

internal readonly record struct WhatsAppFlowSlot(string Type, bool Files, bool Repeated, StructuredFormRowSettings? Rows);

internal sealed record WhatsAppFlowScreen(string Id, string Kind, IReadOnlyList<int> Questions, int Row);
