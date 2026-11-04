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

    public static string Build(IReadOnlyList<StructuredFormQuestion> questions, List<Dictionary<string, object>> controls,
        Dictionary<string, object> initialData, string? title, string? submit, string culture)
    {
        var groups = Groups(questions.Select(question => question.Files is not null).ToArray());
        var owners = groups.SelectMany((group, screen) => group.Select(index => (index, screen))).ToDictionary(value => value.index, value => value.screen);
        var screens = new List<Dictionary<string, object?>>();
        for (var screen = 0; screen < groups.Count; screen++)
        {
            var current = screen;
            var last = screen == groups.Count - 1;
            var children = groups[screen].Select(index => controls[index]).ToList();
            if (screen > 0)
                foreach (var index in groups[screen].Where(index => questions[index].SourceCatalogId.HasValue))
                    controls[index]["data-source"] = "${screen.FORM.data." + questions[index].Key + "_options}";
            object action = last
                ? new { name = "complete", payload = questions.Select((question, index) => new { question.Key, Value = owners[index] == current
                    ? "${form." + question.Key + "}" : "${screen." + ScreenId(owners[index]) + ".form." + question.Key + "}" }).ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal) }
                : new { name = "navigate", next = new { type = "screen", name = ScreenId(screen + 1) }, payload = new Dictionary<string, string>() };
            children.Add(new() { ["type"] = "Footer", ["label"] = last ? submit ?? "" : FlowTexts.Get("Continue", culture), ["on-click-action"] = action });
            var form = new Dictionary<string, object?> { ["type"] = "Form", ["name"] = "form", ["children"] = children };
            var scalarQuestions = groups[screen].Where(index => questions[index].Files is null && questions[index].Type != "multiChoice").Select(index => questions[index]).ToArray();
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

    internal static int RowCount(StructuredFormRowSettings rows) => Math.Min(Configuration.WhatsAppFlowProfile.MaxRows, rows.MaxRows);

    // From the minimum, a row ends with «¿Agregás otro renglón?»; the last one asks nothing.
    internal static bool Asks(StructuredFormRowSettings rows, int row) => row >= Math.Max(rows.MinRows, 1) && row < RowCount(rows);

    public static string BuildRows(IReadOnlyList<StructuredFormQuestion> questions, IReadOnlyList<WhatsAppFlowScreen> screens,
        List<Dictionary<string, object>> controls, Dictionary<string, object> initialData, string? title, string? blockTitle, string? submit, string culture)
    {
        var block = questions.Select((question, index) => (question, index)).First(value => value.question.Type == "group");
        var rows = block.question.Rows!;
        var after = screens.FirstOrDefault(screen => screen.Kind == "fields" && screen.Questions[0] > block.index);
        var built = new List<Dictionary<string, object?>>();
        for (var current = 0; current < screens.Count; current++)
        {
            var screen = screens[current];
            var terminal = false;
            var children = new List<Dictionary<string, object>>();
            var form = new Dictionary<string, object?> { ["type"] = "Form", ["name"] = "form", ["children"] = children };
            if (screen.Kind == "fields")
            {
                children.AddRange(screen.Questions.Select(index => Control(index, current)));
                children.Add(current == screens.Count - 1 ? Footer(submit, Complete()) : Footer(FlowTexts.Get("Continue", culture), Navigate(screens[current + 1].Id)));
                var scalars = screen.Questions.Where(index => questions[index].Files is null && questions[index].Type != "multiChoice").Select(index => questions[index]).ToArray();
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
                children.Add(Branch(screens[current + 1].Id));
            }
            else
            {
                children.AddRange(screen.Questions.Select(index => Control(index, current)));
                if (Asks(rows, screen.Row))
                {
                    children.Add(Question("AddAnotherRow"));
                    children.Add(Branch(screens[current + 1].Id));
                }
                else if (screen.Row < RowCount(rows)) children.Add(Footer(FlowTexts.Get("Continue", culture), Navigate(screens[current + 1].Id)));
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
            built.Add(item);

            Dictionary<string, object> Control(int index, int at)
            {
                var control = new Dictionary<string, object>(controls[index], StringComparer.Ordinal);
                if (at > 0 && questions[index].SourceCatalogId.HasValue) control["data-source"] = "${screen.FORM.data." + questions[index].Key + "_options}";
                return control;
            }
            Dictionary<string, object> Question(string text) => new(StringComparer.Ordinal)
            {
                ["type"] = "RadioButtonsGroup", ["name"] = block.question.Key, ["label"] = FlowTexts.Get(text, culture), ["required"] = true,
                ["data-source"] = new[] { new { id = "true", title = FlowTexts.Get("Yes", culture) }, new { id = "false", title = FlowTexts.Get("No", culture) } },
            };
            Dictionary<string, object> Branch(string next) => new(StringComparer.Ordinal)
            {
                ["type"] = "If", ["condition"] = "${form." + block.question.Key + "} == 'true'",
                ["then"] = new[] { Footer(FlowTexts.Get("Continue", culture), Navigate(next)) }, ["else"] = new[] { Exit() },
            };
            Dictionary<string, object> Exit() => after is null ? Footer(submit, Complete()) : Footer(FlowTexts.Get("Continue", culture), Navigate(after.Id));
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
        return JsonSerializer.Serialize(new { version = "7.3", screens = built });

        static Dictionary<string, object> Footer(string? label, object action) => new(StringComparer.Ordinal)
        {
            ["type"] = "Footer", ["label"] = label ?? "", ["on-click-action"] = action,
        };
        static object Navigate(string next) => new { name = "navigate", next = new { type = "screen", name = next }, payload = new Dictionary<string, string>() };
    }
}

internal readonly record struct WhatsAppFlowSlot(string Type, bool Files, bool Repeated, StructuredFormRowSettings? Rows);

internal sealed record WhatsAppFlowScreen(string Id, string Kind, IReadOnlyList<int> Questions, int Row);
