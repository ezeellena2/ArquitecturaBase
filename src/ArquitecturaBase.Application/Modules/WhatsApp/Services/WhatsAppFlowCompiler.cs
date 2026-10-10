using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        if (request.Format != 1 || request.OriginalCulture is not ("es" or "en") || request.Questions.Count > 40
            || request.TargetCultures.Count == 0 || request.TargetCultures.Distinct(StringComparer.Ordinal).Count() != request.TargetCultures.Count
            || request.TargetCultures.Any(x => x is not ("es" or "en")))
            return new([], [new(null, null, "draft", FlowErrors.UnsupportedConfiguration)]);

        var ids = new HashSet<Guid>();
        var choices = new HashSet<Guid>();
        foreach (var question in request.Questions)
        {
            if (question.FieldId == Guid.Empty || !ids.Add(question.FieldId) || question.Key != "field_" + question.FieldId.ToString("N")
                || question.Type is not ("shortText" or "longText" or "decimal" or "date" or "boolean" or "choice")
                || (question.Type is "shortText" or "longText" ? question.MaxLength is not (>= 1 and <= 10000) : question.MaxLength.HasValue)
                || (question.Type == "choice" ? question.Options is not { Count: >= 1 and <= 50 } : question.Options is not null))
                issues.Add(new(question.FieldId, null, "draft.fields", FlowErrors.UnsupportedConfiguration));
            foreach (var option in question.Options ?? [])
                if (option.ChoiceId == Guid.Empty || !choices.Add(option.ChoiceId)) issues.Add(new(question.FieldId, option.ChoiceId, "draft.fields", FlowErrors.UnsupportedConfiguration));
        }
        if (issues.Count > 0) return new([], issues.AsReadOnly());
        foreach (var culture in request.TargetCultures.Order(StringComparer.Ordinal))
        {
            var components = new List<Dictionary<string, object>>();
            var mapping = new List<WhatsAppFlowFieldMapping>();
            var name = Resolve(request.Name, "name", null, null, 80);
            var submit = Resolve(request.SubmitLabel, "draft.submitLabel", null, null, 35);
            for (var index = 0; index < request.Questions.Count; index++)
            {
                var question = request.Questions[index];
                var path = $"draft.fields[{index}]";
                var labelMax = question.Type == "date" ? 40 : question.Type == "boolean" ? 30 : 20;
                var label = Resolve(question.Label, path + ".label", question.FieldId, null, labelMax);
                var helpMax = question.Type == "boolean" ? 300 : 80;
                var help = question.Help is null ? null : Resolve(question.Help, path + ".help", question.FieldId, null, helpMax, question.Help.Values.Any(value => !string.IsNullOrWhiteSpace(value)));
                if (question.Type == "choice" && help is not null)
                    issues.Add(new(question.FieldId, null, path + ".help", FlowErrors.UnsupportedConfiguration, Culture: culture));
                var control = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["type"] = question.Type switch { "shortText" or "decimal" => "TextInput", "longText" => "TextArea", "date" => "CalendarPicker", "boolean" => "RadioButtonsGroup", _ => "Dropdown" },
                    ["name"] = question.Key, ["label"] = label ?? "", ["required"] = question.Required,
                };
                if (help is not null) control[question.Type == "boolean" ? "description" : "helper-text"] = help;
                if (question.Type is "shortText" or "longText") control[question.Type == "shortText" ? "max-chars" : "max-length"] = question.MaxLength!.Value;
                if (question.Type == "decimal")
                {
                    control["input-type"] = "text"; control["pattern"] = ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.DecimalPattern;
                    control["max-chars"] = ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowProfile.DecimalMaxChars;
                    if (help is null) control["helper-text"] = FlowTexts.Get("DecimalHelp", culture);
                }
                if (question.Type == "date") control["mode"] = "single";
                if (question.Type == "boolean") control["data-source"] = new[] { new { id = "true", title = FlowTexts.Get("Yes", culture) }, new { id = "false", title = FlowTexts.Get("No", culture) } };
                if (question.Options is { } options)
                    control["data-source"] = options.Select((option, optionIndex) => new
                    {
                        id = option.ChoiceId.ToString("D"),
                        title = Resolve(option.Label, $"{path}.options[{optionIndex}].label", question.FieldId, option.ChoiceId, 30) ?? "",
                    }).ToArray();
                components.Add(control);
                mapping.Add(new(question.FieldId, question.Key, question.Type, question.Required, question.MaxLength,
                    (question.Options?.Select(option => option.ChoiceId).ToList() ?? []).AsReadOnly()));
            }
            components.Add(new(StringComparer.Ordinal) { ["type"] = "Footer", ["label"] = submit ?? "", ["on-click-action"] = new
            {
                name = "complete", payload = request.Questions.ToDictionary(question => question.Key, question => "${form." + question.Key + "}", StringComparer.Ordinal),
            } });
            var count = components.Count + 1; // Includes the Form container, conservatively.
            if (count > 50) issues.Add(new(null, null, "draft.fields", FlowErrors.ComponentBudgetExceeded, 50, culture));
            if (issues.Count > 0) continue;
            var json = JsonSerializer.Serialize(new { version = "7.3", screens = new[] { new
            {
                id = "FORM", title = name, terminal = true, layout = new { type = "SingleColumnLayout", children = new[] { new { type = "Form", name = "form", children = components } } },
            } } });
            var bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.Length > 1048576) { issues.Add(new(null, null, "draft", FlowErrors.UnsupportedConfiguration)); continue; }
            flows.Add(new(culture, json, Convert.ToHexStringLower(SHA256.HashData(bytes)), count, mapping.AsReadOnly()));

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
