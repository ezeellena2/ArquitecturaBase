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
}
