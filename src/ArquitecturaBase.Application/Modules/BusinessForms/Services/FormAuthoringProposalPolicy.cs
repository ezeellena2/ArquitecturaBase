using System.Globalization;
using System.Text;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// Turns what the assistant proposes into the same requests the authoring uses, so both obey one set of rules.
internal static class FormAuthoringProposalPolicy
{
    public static readonly string[] QuestionTypes = ["shortText", "longText", "number", "date", "yesNo", "choice", "multiChoice", "catalog", "photo", "document"];
    public static readonly string[] FieldTypes = ["text", "integer", "decimal", "date", "boolean"];
    public static readonly string[] ValueTypes = ["text", "integer", "decimal", "date"];

    public static FormAuthoringFormProposal Normalize(FormAuthoringFormProposal proposal) => proposal with
    {
        Name = proposal.Name.Trim(), Prefix = proposal.Prefix.Trim().ToUpperInvariant(),
        SubmitLabel = string.IsNullOrWhiteSpace(proposal.SubmitLabel) ? proposal.Culture == "en" ? "Submit" : "Enviar" : proposal.SubmitLabel.Trim(),
        Questions = proposal.Questions.Select(question => question with
        {
            Label = question.Label.Trim(), Options = question.Options.Select(option => option.Trim()).ToArray(),
        }).ToArray(),
    };

    public static bool HasValidShape(FormAuthoringFormProposal proposal) => proposal.Culture is "es" or "en" && ShapeIssues(proposal).Count == 0;

    // A question's kind decides its options and catalog; the authoring validator checks everything else.
    public static IReadOnlyList<FormAuthoringIssue> ShapeIssues(FormAuthoringFormProposal proposal)
    {
        if (proposal.Questions.Count is < 1 or > 40) return [new("questions", "invalid")];
        var issues = new List<FormAuthoringIssue>();
        for (var index = 0; index < proposal.Questions.Count; index++)
        {
            var question = proposal.Questions[index];
            if (!QuestionTypes.Contains(question.Type, StringComparer.Ordinal)) { issues.Add(new($"questions[{index}].type", "invalid")); continue; }
            if (question.Type is "choice" or "multiChoice" ? question.Options.Count == 0 : question.Options.Count != 0)
                issues.Add(new($"questions[{index}].options", "invalid"));
            if (question.Type == "catalog" ? question.Catalog is not { Id: var id } || id == Guid.Empty : question.Catalog is not null)
                issues.Add(new($"questions[{index}].catalogId", "invalid"));
        }
        return issues;
    }

    // Etapa 3 de la IA: a reading the proposal turned on goes on in the draft, with its «Cómo reconocerlo» and no lookups.
    public static SaveFormDraftRequest Draft(Guid requestId, FormAuthoringFormProposal proposal, FormAuthoringProposedReading? reading = null) => new(requestId,
        new Dictionary<string, string> { [proposal.Culture] = proposal.Name }, proposal.Prefix,
        new FormDraftInput(1, proposal.Culture, new Dictionary<string, string> { [proposal.Culture] = proposal.SubmitLabel },
            proposal.Questions.Select((question, index) => Field("q" + (index + 1), question, proposal.Culture)).ToArray(),
            DocumentReading: reading is null ? null : new FormDocumentReading(true, reading.Recognition, [])));

    // Version 2 creates version 1's form with its reading; its samples and its standard are never kept.
    public static SaveFormDraftRequest Draft(Guid requestId, FormAuthoringDocumentFormProposal proposal) =>
        Draft(requestId, proposal.ToForm(), proposal.DocumentReading);

    // The authoring validator names draft paths; the model fixes the arguments it wrote.
    public static string IssuePath(string draftPath)
    {
        const string fields = "draft.fields[";
        if (draftPath.StartsWith(fields, StringComparison.Ordinal) && draftPath.IndexOf(']', fields.Length) is var end and > 0)
            return "questions[" + draftPath[fields.Length..end] + "]";
        if (draftPath == "draft.fields") return "questions";
        if (draftPath.StartsWith("name", StringComparison.Ordinal)) return "name";
        if (draftPath.StartsWith("draft.submitLabel", StringComparison.Ordinal)) return "submitLabel";
        if (draftPath.StartsWith("draft.documentReading", StringComparison.Ordinal)) return draftPath["draft.".Length..];
        return draftPath;
    }

    // A sample is what the document shows for one question, on one line and only what is seen: control characters and line or
    // paragraph separators become spaces, every run of spaces one, and the invisible format characters go (bidirectional overrides,
    // zero-width spaces, the byte order mark, soft hyphens), except the joiners an emoji or a script needs, as the draft's texts allow.
    // Without anything visible there is no sample. Longer than its limit, or not storable, the model writes it again.
    public static string? Sample(string? text)
    {
        if (text is null) return null;
        var kept = new StringBuilder(text.Length);
        var visible = false;
        for (var index = 0; index < text.Length; index++)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(text, index);
            var pair = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]);
            var value = pair ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];
            if (category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) kept.Append(' ');
            else if (category != UnicodeCategory.Format || value is 0x200C or 0x200D)
            {
                kept.Append(text, index, pair ? 2 : 1);
                visible |= category is not (UnicodeCategory.Format or UnicodeCategory.SpaceSeparator or UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark);
            }
            if (pair) index++;
        }
        var inline = string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return visible && inline.Length > 0 ? inline : null;
    }

    public static bool ValidSample(string sample) => StringInfo.ParseCombiningCharacters(sample).Length <= FormAuthoringDocumentFormProposal.MaxSampleLength
        && StorableText.IsStorable(sample);

    // Version 2's own parts as the server proposed them: «Cómo reconocerlo» within its limit, each sample normalized and within its
    // limit, and the standard with its key and name.
    public static bool HasValidDocumentParts(FormAuthoringDocumentFormProposal proposal) =>
        proposal.DocumentReading is null or { Recognition.Length: > 0 and <= FormDocumentReading.MaxRecognitionLength }
        && proposal.Questions.All(question => question.Sample is null || question.Sample == Sample(question.Sample) && ValidSample(question.Sample))
        && proposal.Standard is null or { Key.Length: > 0, Name.Length: > 0 };

    private static FormFieldInput Field(string localId, FormAuthoringProposedQuestion question, string culture)
    {
        var label = new Dictionary<string, string> { [culture] = question.Label };
        var minimum = question.Required ? 1 : 0;
        return question.Type switch
        {
            "number" => new(localId, null, null, "decimal", label, null, question.Required, null, null),
            "yesNo" => new(localId, null, null, "boolean", label, null, question.Required, null, null),
            "choice" => new(localId, null, null, "choice", label, null, question.Required, null, Options(localId, question.Options, culture)),
            "multiChoice" => new(localId, null, null, "multiChoice", label, null, question.Required, null, Options(localId, question.Options, culture),
                Selection: new(minimum, question.Options.Count)),
            "catalog" => new(localId, null, null, "choice", label, null, question.Required, null, null, Source: new(question.Catalog!.Id)),
            "photo" or "document" => new(localId, null, null, question.Type, label, null, question.Required, null, null,
                Files: new(minimum, 1, question.Type == "photo" ? "cameraOrGallery" : null)),
            _ => new(localId, null, null, question.Type, label, null, question.Required, null, null),
        };
    }

    private static FormChoiceInput[] Options(string localId, IReadOnlyList<string> options, string culture) =>
        options.Select((option, index) => new FormChoiceInput(localId + "o" + (index + 1), null,
            new Dictionary<string, string> { [culture] = option })).ToArray();

    public static FormAuthoringCatalogProposal Normalize(FormAuthoringCatalogProposal proposal) => proposal with
    {
        Name = proposal.Name.Trim(), Description = proposal.Description.Trim(), Code = proposal.Code.Trim(), Option = proposal.Option.Trim(),
        Fields = proposal.Fields.Select(field => field with { Name = field.Name.Trim() }).ToArray(),
    };

    public static IReadOnlyList<FormAuthoringIssue> Issues(FormAuthoringCatalogProposal proposal)
    {
        var issues = new List<FormAuthoringIssue>();
        if (proposal.Name.Length is 0 or > 120) issues.Add(new("name", "invalid"));
        if (proposal.Description.Length > 1000) issues.Add(new("description", "invalid"));
        if (!ValueTypes.Contains(proposal.ValueType, StringComparer.Ordinal)) issues.Add(new("valueType", "invalid"));
        if (proposal.Code.Length is 0 or > 80) issues.Add(new("code", "invalid"));
        if (proposal.Option.Length is 0 or > 80) issues.Add(new("option", "invalid"));
        if (proposal.Fields.Count > BusinessFormCatalog.MaxFields) issues.Add(new("fields", "invalid"));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < proposal.Fields.Count; index++)
            if (proposal.Fields[index].Name.Length is 0 or > 120 || !FieldTypes.Contains(proposal.Fields[index].Type, StringComparer.Ordinal) ||
                !names.Add(proposal.Fields[index].Name)) issues.Add(new($"fields[{index}]", "invalid"));
        return proposal.Culture is "es" or "en" ? issues : [new("culture", "invalid")];
    }

    // The fields of a proposed Manual catalog, in its order: born optional and as data, like a new field in the editor.
    public static IReadOnlyList<BusinessFormCatalogField> CatalogFields(FormAuthoringCatalogProposal proposal)
    {
        var keys = new List<string>();
        var fields = new List<BusinessFormCatalogField>();
        foreach (var field in proposal.Fields)
        {
            keys.Add(FieldKey(field.Name, keys));
            fields.Add(new(BusinessFormCatalogField.CreateIdentity(), keys[^1], new Dictionary<string, string> { [proposal.Culture] = field.Name },
                field.Type, null, false, false, null, null, null, "data", fields.Count, true));
        }
        return fields;
    }

    // A field's key from its name, as the editor makes it: lowercase without accents, anything else as one "_" and never at
    // the ends, starting with a letter and without repeating another key ("_2", "_3"…).
    public static string FieldKey(string name, IReadOnlyCollection<string> taken)
    {
        const int maximum = 40;
        var plain = name.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var key = new StringBuilder();
        foreach (var character in plain.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
        {
            if (character is >= 'a' and <= 'z' || (character is >= '0' and <= '9' && key.Length > 0)) key.Append(character);
            else if (key.Length > 0 && key[^1] != '_') key.Append('_');
        }
        var root = key.ToString(0, Math.Min(key.Length, maximum)).TrimEnd('_');
        if (root.Length == 0) root = "field";
        var candidate = root;
        for (var suffix = 2; taken.Contains(candidate); suffix++)
            candidate = root[..Math.Min(root.Length, maximum - ("_" + suffix).Length)].TrimEnd('_') + "_" + suffix;
        return candidate;
    }
}
