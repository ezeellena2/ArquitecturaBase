using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal static class WhatsAppFlowCompiler
{
    public const string ProfileRevision = ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.Revision;
    public const string GraphApiVersion = ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.GraphApiVersion;
    public const int CodecVersion = ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.CodecVersion;

    public static WhatsAppFlowCompilationResponse Compile(StructuredFormCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issues = new List<StructuredFormIssueResponse>();
        var flows = new List<WhatsAppCompiledFlowResponse>();
        // The block of rows is not one of the 40 questions (RG1-A).
        if (request.MaxFileSizeMiB is < 1 or > 25 || request.Format != 1 || request.OriginalCulture is not ("es" or "en")
            || request.Questions.Count(question => question.Type != "group") > 40 || request.Questions.Count > 41
            || request.TargetCultures.Count == 0 || request.TargetCultures.Distinct(StringComparer.Ordinal).Count() != request.TargetCultures.Count
            || request.TargetCultures.Any(x => x is not ("es" or "en")))
            return new([], [new(null, null, "draft", FlowErrors.UnsupportedConfiguration)]);

        if (request.Questions.Sum(question => question.Files?.MaxFiles ?? 0) > 10)
            return new([], [new(null, null, "draft.fields", FlowErrors.UnsupportedConfiguration, 10)]);
        var ids = new HashSet<Guid>();
        var choices = new HashSet<Guid>();
        foreach (var question in request.Questions)
        {
            var invalidChoice = question.Type is "choice" or "multiChoice"
                ? question.SourceCatalogId is { } catalogId
                    ? catalogId == Guid.Empty || question.Options is not null
                    : question.Options is null || question.Options.Count < 1 || question.Options.Count > (question.Type == "multiChoice" ? 20 : 50)
                : question.SourceCatalogId.HasValue || question.Options is not null;
            if (question.FieldId == Guid.Empty || !ids.Add(question.FieldId) || question.Key != "field_" + question.FieldId.ToString("N")
                || question.Type is not ("shortText" or "longText" or "decimal" or "date" or "boolean" or "choice" or "multiChoice" or "photo" or "document" or "group")
                || (question.Type is "shortText" or "longText" ? question.MaxLength is not (>= 1 and <= 10000) : question.MaxLength.HasValue)
                || invalidChoice || !WhatsAppFlowScreenBuilder.ValidFiles(question) || !WhatsAppFlowScreenBuilder.ValidSelection(question)
                || !WhatsAppFlowScreenBuilder.ValidRows(question)
                // Choosing first by a field (LD2-A) is for a single choice from a catalog.
                || (question.ChoosesFirst == true && (question.Type != "choice" || question.SourceCatalogId is null))
                // A calculation (CA1-A) is an optional decimal.
                || (question.Calculated == true && (question.Type != "decimal" || question.Required)))
                issues.Add(new(question.FieldId, null, "draft.fields", FlowErrors.UnsupportedConfiguration));
            foreach (var option in question.Options ?? [])
                if (option.ChoiceId == Guid.Empty || !choices.Add(option.ChoiceId)) issues.Add(new(question.FieldId, option.ChoiceId, "draft.fields", FlowErrors.UnsupportedConfiguration));
        }
        if (issues.Count > 0) return new([], issues.AsReadOnly());
        // One block at most, with its rows' questions right after it and at least one.
        var block = Enumerable.Range(0, request.Questions.Count).FirstOrDefault(index => request.Questions[index].Type == "group", -1);
        var end = block + 1;
        while (block >= 0 && end < request.Questions.Count && request.Questions[end].Repeated == true) end++;
        if (request.Questions.Count(question => question.Type == "group") > 1 || (block >= 0 && end == block + 1)
            || request.Questions.Where((question, index) => question.Repeated == true && (index <= block || index >= end)).Any())
            return new([], [new(null, null, "draft.fields", FlowErrors.UnsupportedConfiguration)]);
        // Each section title has questions below it, in order, and none starts between the block and its questions (CW15-B).
        var sections = request.Sections ?? [];
        if (sections.Count > 40 || sections.Where((section, index) => section is null || section.Title is null || section.Start < 0
                || section.Start >= (index + 1 < sections.Count ? sections[index + 1]?.Start ?? -1 : request.Questions.Count)
                || (block >= 0 && section.Start > block && section.Start < end)).Any())
            return new([], [new(null, null, "draft.sections", FlowErrors.UnsupportedConfiguration)]);
        // CO1-A (r13): a condition names an earlier question outside the block, of choose one or yes or no, and answers it has. A
        // photo or a document does not take one: its picker would go inside an If, which the Flow does not need yet. Several answers
        // need options that set a boolean, and those of a catalog arrive with the message: a catalog question shows another for one
        // answer. Meta nests up to three If, so a chain goes three conditions deep.
        var depths = new int[request.Questions.Count];
        for (var index = 0; index < request.Questions.Count; index++)
        {
            var question = request.Questions[index];
            if (question.ShowWhen is not { } showWhen) continue;
            var controller = Enumerable.Range(0, index).FirstOrDefault(earlier => request.Questions[earlier].FieldId == showWhen.FieldId, -1);
            var source = controller < 0 ? null : request.Questions[controller];
            var values = showWhen.Values;
            depths[index] = controller < 0 ? 1 : depths[controller] + 1;
            var valid = question.Type != "group" && question.Repeated != true && question.Files is null
                && source is { Repeated: not true, Calculated: not true } && values is { Count: >= 1 and <= 50 }
                && values.Distinct(StringComparer.Ordinal).Count() == values.Count && source.Type switch
                {
                    "boolean" => values.All(value => value is "true" or "false"),
                    "choice" when source.SourceCatalogId is not null => values.All(value => Guid.TryParseExact(value, "D", out _)),
                    "choice" => values.All(value => source.Options!.Any(option => option.ChoiceId.ToString("D") == value)),
                    _ => false,
                };
            // The two limits the editor also warns about say what happens; the rest is a configuration the Flow cannot show.
            var error = !valid ? FlowErrors.UnsupportedConfiguration
                : depths[index] > 3 ? FlowErrors.ConditionTooDeep
                : source!.SourceCatalogId is not null && values.Count > 1 ? FlowErrors.ConditionCatalogValues : null;
            if (error is not null) issues.Add(new(question.FieldId, null, $"draft.fields[{index}].showWhen", error));
        }
        if (issues.Count > 0) return new([], issues.AsReadOnly());
        var screens = WhatsAppFlowScreenBuilder.Plan(request.Questions.Select(question =>
            new WhatsAppFlowSlot(question.Type, question.Files is not null, question.Repeated == true, question.Rows, question.Calculated == true)).ToArray());
        var rows = block >= 0 ? request.Questions[block].Rows! : null;
        var rowCount = rows is null ? 0 : WhatsAppFlowScreenBuilder.RowCount(rows);
        // «¿Agregás un ítem?» answers under _r0 and each «¿Agregás otro ítem?» under its row (RG8-A).
        var asked = rows is null ? 0 : (rows.MinRows == 0 ? 1 : 0) + Enumerable.Range(1, rowCount).Count(row => WhatsAppFlowScreenBuilder.Asks(rows, row));
        // Where each title goes (CW15-B): before the first question of its section that the Flow shows, or atop the block. From r13 a
        // title with only calculations below it is not shown, and one whose questions all share a condition shows with them.
        var places = new Dictionary<int, (int At, IReadOnlyList<string>? When)>();
        for (var index = 0; index < sections.Count; index++)
        {
            var stop = index + 1 < sections.Count ? sections[index + 1].Start : request.Questions.Count;
            var shown = Enumerable.Range(sections[index].Start, stop - sections[index].Start)
                .Where(at => request.Questions[at].Calculated != true && request.Questions[at].Repeated != true).ToArray();
            if (shown.Length == 0) continue;
            places[index] = (shown[0], shown.All(at => request.Questions[at].ShowWhen is not null)
                ? WhatsAppFlowScreenBuilder.Shared(request.Questions, screens, shown, WhatsAppFlowScreenBuilder.Owner(screens, shown[0])) : null);
        }
        var guarded = places.Values.Where(place => place.When is not null).ToDictionary(place => place.At, place => place.When!.Count);
        // A calculation is not a component, and each component of a question with a condition, or a title shown with them, goes
        // inside one If per condition (r13).
        int Ifs(IEnumerable<int> indices) => indices.Sum(index => (request.Questions[index] is { ShowWhen: not null, Calculated: not true } question
            ? (question.ChoosesFirst == true ? 2 : 1) * depths[index] : 0) + guarded.GetValueOrDefault(index));
        foreach (var culture in request.TargetCultures.Order(StringComparer.Ordinal))
        {
            var worstCaseBytes = 2L + 12 + 1 + 2 + 128L * 6;
            for (var index = 0; index < request.Questions.Count; index++)
            {
                var question = request.Questions[index];
                // A calculation never arrives (CA1-A).
                if (question.Calculated == true) continue;
                var valueBytes = question.Type switch
                {
                    "shortText" or "longText" => 2L + question.MaxLength!.Value * 6L,
                    "decimal" => 2L + ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.DecimalMaxChars,
                    "date" => 12L,
                    "boolean" or "group" => 7L,
                    "choice" => 38L,
                    "multiChoice" => 2L + question.Selection!.MaxSelected * 39L,
                    "photo" or "document" => 2L + (question.Files?.MaxFiles ?? 0) * 2048L,
                    _ => 0L,
                };
                // A row's answers repeat under keys up to four characters longer (_r10).
                worstCaseBytes += question.Type == "group" ? asked * (46L + valueBytes)
                    : question.Repeated == true ? rowCount * (46L + valueBytes) : 42L + valueBytes;
            }
            // RG7-A: with a block the response has the room of a Flow with files.
            var responseBudget = block >= 0 || request.Questions.Any(question => question.Files is not null)
                ? ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.MaxMediaResponseJsonBytes
                : ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.MaxResponseJsonBytes;
            if (worstCaseBytes > responseBudget)
            {
                for (var index = 0; index < request.Questions.Count; index++)
                {
                    var question = request.Questions[index];
                    if (question.Type is "shortText" or "longText" or "multiChoice")
                    {
                        var path = question.Type == "multiChoice" ? $"draft.fields[{index}].selection.maxSelected" : $"draft.fields[{index}].maxLength";
                        issues.Add(new(question.FieldId, null, path, FlowErrors.ResponseTooLarge, Culture: culture));
                    }
                }
            }
            var components = new List<Dictionary<string, object>>();
            var screenData = new Dictionary<string, object>(StringComparer.Ordinal);
            var mapping = new List<WhatsAppFlowFieldMapping>();
            var name = Resolve(request.Name, "name", null, null, 80);
            var submit = Resolve(request.SubmitLabel, "draft.submitLabel", null, null, 35);
            // A title is a subtitle before the first question of its section, keyed by that question's position.
            var subtitles = new Dictionary<int, Dictionary<string, object>>();
            for (var index = 0; index < sections.Count; index++)
            {
                var subtitle = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = "TextSubheading", ["text"] = Resolve(sections[index].Title, $"draft.sections[{index}].title", null, null, 80) ?? "",
                };
                if (places.TryGetValue(index, out var place))
                    subtitles[place.At] = place.When is null ? subtitle : WhatsAppFlowScreenBuilder.Nest(place.When, subtitle);
            }
            string? blockTitle = null;
            for (var index = 0; index < request.Questions.Count; index++)
            {
                var question = request.Questions[index];
                var path = $"draft.fields[{index}]";
                mapping.Add(new(question.FieldId, question.Key, question.Type, question.Required, question.MaxLength,
                    (question.Options?.Select(option => option.ChoiceId).ToList() ?? []).AsReadOnly(), question.Files, question.Selection,
                    question.Rows, question.Repeated == true ? true : null, question.ChoosesFirst == true ? true : null,
                    question.Calculated == true ? true : null, question.ShowWhen));
                // The server computes a calculation (CA1-A): the Flow neither shows it nor needs its texts.
                if (question.Calculated == true)
                {
                    components.Add(new(StringComparer.Ordinal));
                    continue;
                }
                var labelMax = question.Type == "group" ? 60 : question.Files is not null ? 80 : question.Type == "date" ? 40 : question.Type is "boolean" or "multiChoice" ? 30 : 20;
                var label = Resolve(question.Label, path + ".label", question.FieldId, null, labelMax);
                if (question.Type == "group")
                {
                    // The block titles its screens; WhatsApp does not show its help.
                    blockTitle = label;
                    components.Add(new(StringComparer.Ordinal));
                    continue;
                }
                var helpMax = question.Files is not null || question.Type is "boolean" or "multiChoice" ? 300 : 80;
                var help = question.Help is null ? null : Resolve(question.Help, path + ".help", question.FieldId, null, helpMax, question.Help.Values.Any(value => !string.IsNullOrWhiteSpace(value)));
                if (question.Type == "choice" && help is not null)
                    issues.Add(new(question.FieldId, null, path + ".help", FlowErrors.UnsupportedConfiguration, Culture: culture));
                var control = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = question.Type switch { "shortText" or "decimal" => "TextInput", "longText" => "TextArea", "date" => "CalendarPicker", "boolean" => "RadioButtonsGroup", "multiChoice" => "CheckboxGroup", "photo" => "PhotoPicker", "document" => "DocumentPicker", _ => "Dropdown" },
                    ["name"] = question.Key, ["label"] = label ?? "", ["required"] = question.Required,
                };
                if (help is not null) control[question.Type is "boolean" or "multiChoice" || question.Files is not null ? "description" : "helper-text"] = help;
                if (question.Files is not null) WhatsAppFlowScreenBuilder.ConfigurePicker(control, question, request.MaxFileSizeMiB ?? 25);
                if (question.Type is "shortText" or "longText") control[question.Type == "shortText" ? "max-chars" : "max-length"] = question.MaxLength!.Value;
                if (question.Type == "decimal")
                {
                    control["input-type"] = "text"; control["pattern"] = ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.DecimalPattern;
                    control["max-chars"] = ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.DecimalMaxChars;
                    if (help is null) control["helper-text"] = FlowTexts.Get("DecimalHelp", culture);
                }
                if (question.Type == "multiChoice")
                {
                    control["min-selected-items"] = question.Selection!.MinSelected;
                    control["max-selected-items"] = question.Selection.MaxSelected;
                    if (help is null) control["description"] = string.Format(CultureInfo.GetCultureInfo(culture),
                        FlowTexts.Get("SelectUpTo", culture), question.Selection.MaxSelected);
                }
                if (question.Type == "date") control["mode"] = "single";
                if (question.Type == "boolean") control["data-source"] = new[] { Option(index, "true", FlowTexts.Get("Yes", culture)), Option(index, "false", FlowTexts.Get("No", culture)) };
                if (question.SourceCatalogId.HasValue)
                {
                    control["data-source"] = "${data." + question.Key + "_options}";
                    screenData[question.Key + "_options"] = WhatsAppFlowScreenBuilder.OptionsSchema();
                    if (question.ChoosesFirst == true) WhatsAppFlowScreenBuilder.ChooseFirst(control, question, screenData, culture);
                }
                else if (question.Options is { } options)
                    control["data-source"] = options.Select((option, optionIndex) => Option(index, option.ChoiceId.ToString("D"),
                        Resolve(option.Label, $"{path}.options[{optionIndex}].label", question.FieldId, option.ChoiceId, 30) ?? "")).ToArray();
                components.Add(control);
            }
            components.Add(new(StringComparer.Ordinal) { ["type"] = "Footer", ["label"] = submit ?? "", ["on-click-action"] = new
            {
                name = "complete", payload = request.Questions.ToDictionary(question => question.Key, question => "${form." + question.Key + "}", StringComparer.Ordinal),
            } });
            // Includes the Form container, conservatively. With a block the limit applies to each screen: a row adds its own
            // question, an If and a Footer in each branch. The dropdown of values of a question that chooses first is one more.
            var count = rows is null ? components.Count - request.Questions.Count(question => question.Calculated == true) + 1 + subtitles.Count
                    + request.Questions.Count(question => question.ChoosesFirst == true) + Ifs(Enumerable.Range(0, request.Questions.Count))
                : screens.Max(screen => WhatsAppFlowScreenBuilder.Subtitled(screen, block, rows, subtitles)
                    + screen.Questions.Count(index => request.Questions[index].ChoosesFirst == true) + screen.Kind switch
            {
                "fields" => screen.Questions.Count + 2 + Ifs(screen.Questions),
                "ask" => 5,
                "end" => 3,
                _ => screen.Questions.Count + (WhatsAppFlowScreenBuilder.Asks(rows, screen.Row) ? 4 : 1) + 1,
            });
            if (count > 50) issues.Add(new(null, null, "draft.fields", FlowErrors.ComponentBudgetExceeded, 50, culture));
            if (issues.Count > 0) continue;
            var json = rows is null
                ? WhatsAppFlowScreenBuilder.Build(request.Questions, screens, components, screenData, name, submit, culture, subtitles)
                : WhatsAppFlowScreenBuilder.BuildRows(request.Questions, screens, components, screenData, name, blockTitle, submit, culture, subtitles);
            var bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.Length > 1048576) { issues.Add(new(null, null, "draft", FlowErrors.UnsupportedConfiguration)); continue; }
            flows.Add(new(culture, json, Convert.ToHexStringLower(SHA256.HashData(bytes)), count, mapping.AsReadOnly()));

            // An option of a question that shows another by several answers sets that one's boolean (CO1-A).
            Dictionary<string, object> Option(int index, string id, string title)
            {
                var option = new Dictionary<string, object>(StringComparer.Ordinal) { ["id"] = id, ["title"] = title };
                if (WhatsAppFlowScreenBuilder.OnSelect(request.Questions, index, id) is { } onSelect) option["on-select-action"] = onSelect;
                return option;
            }

            string? Resolve(IReadOnlyDictionary<string, string> texts, string path, Guid? fieldId, Guid? choiceId, int maximum, bool required = true)
            {
                if (!texts.TryGetValue(culture, out var text) || string.IsNullOrWhiteSpace(text))
                {
                    if (required) issues.Add(new(fieldId, choiceId, path + "." + culture, FlowErrors.MissingTranslation, Culture: culture));
                    return null;
                }
                if (text.Length > maximum) issues.Add(new(fieldId, choiceId, path + "." + culture, FlowErrors.TextTooLong, maximum, culture));
                return text;
            }
        }
        return new(issues.Count == 0 ? flows.AsReadOnly() : [], issues.AsReadOnly());
    }
}
