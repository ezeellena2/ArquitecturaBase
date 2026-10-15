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
        List<List<int>> groups = [[]];
        for (var index = 0; index < questions.Count; index++)
        {
            if (questions[index].Files is not null && groups[^1].Count > 0) groups.Add([]);
            groups[^1].Add(index);
        }
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
            var item = new Dictionary<string, object?> { ["id"] = ScreenId(screen), ["title"] = title,
                ["layout"] = new { type = "SingleColumnLayout", children = new[] { new { type = "Form", name = "form", children } } } };
            if (last) item["terminal"] = true;
            if (screen == 0 && initialData.Count > 0) item["data"] = initialData;
            screens.Add(item);
        }
        return JsonSerializer.Serialize(new { version = "7.3", screens });
    }

    private static string ScreenId(int index) => index == 0 ? "FORM" : "FORM_" + (index + 1).ToString(CultureInfo.InvariantCulture);
}
