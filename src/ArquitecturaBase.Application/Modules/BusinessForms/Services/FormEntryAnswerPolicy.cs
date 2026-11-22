using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// A saved response in the shape the app loads it: its answers as the form sends them and its files, on the questions of a version.
/// A correction (CW10-A) puts them on the active version; the detail (VF1-A), on the response's own.
/// </summary>
internal static class FormEntryAnswerPolicy
{
    /// <summary>
    /// The answers on the questions <paramref name="version"/> keeps, then the items of its block. <paramref name="values"/> are the
    /// response's, projected on its own <paramref name="recorded"/> version in <paramref name="culture"/>.
    /// </summary>
    public static List<FormEntryCorrectionAnswer> Answers(FormVersionSchema version, FormVersionSchema recorded, FormRecordStoredRow record,
        IReadOnlyList<StructuredRecordReadValue> values, string culture)
    {
        var answers = values.Select(value => version.Questions.SingleOrDefault(question => question.FieldId == value.FieldId) is { } question
            ? Answer(question, value.Value) : null).OfType<FormEntryCorrectionAnswer>().ToList();
        if (BlockAnswer(version, recorded, record, culture) is { } block) answers.Add(block);
        return answers;
    }

    /// <summary>The files of the response on the photo and document questions <paramref name="version"/> keeps, in their order.</summary>
    public static FormEntryCorrectionFile[] Files(FormVersionSchema version, FormRecordStoredRow record) =>
        (record.Files ?? []).Where(file => version.Questions.Any(question => question.FieldId == file.FieldId && question.Files is not null))
            .OrderBy(file => file.FieldId).ThenBy(file => file.Position)
            .Select(file => new FormEntryCorrectionFile(file.FileId, file.FieldId, file.Name, file.ContentType, file.Size, file.HasThumbnail)).ToArray();

    // The items of the block as the form sends them, when the version keeps that block: each item with its answers to the
    // questions that remain in it. A block the version no longer has does not come back.
    private static FormEntryCorrectionAnswer? BlockAnswer(FormVersionSchema version, FormVersionSchema recorded, FormRecordStoredRow record, string culture)
    {
        var group = version.Questions.FirstOrDefault(question => question.Type == "group");
        if (group is null || !recorded.Questions.Any(question => question.FieldId == group.FieldId && question.Type == "group")) return null;
        var block = version.Questions.SkipWhile(question => question.FieldId != group.FieldId).Skip(1).TakeWhile(question => question.Repeated == true).ToArray();
        return new(group.FieldId, null, null, null)
        {
            Rows = FormRecordPresentationPolicy.Rows(record, culture, recorded).Select(item => (IReadOnlyList<FormEntryCorrectionAnswer>)item.Values
                .Select(value => block.SingleOrDefault(question => question.FieldId == value.FieldId) is { } question ? Answer(question, value.Value) : null)
                .OfType<FormEntryCorrectionAnswer>().ToArray()).ToArray(),
        };
    }

    // An answer as the form sends it: canonical text, or the options chosen; a choice the version no longer has is dropped.
    private static FormEntryCorrectionAnswer? Answer(CanonicalFormField question, object? value)
    {
        IReadOnlyList<StructuredRecordChoiceValue>? choices = value switch
        {
            StructuredRecordChoiceValue choice => [choice],
            StructuredRecordChoiceValue[] many => many,
            _ => null,
        };
        if (choices is not null)
        {
            var catalog = question.Source?.Catalog is not null;
            var kept = choices.Where(choice => catalog || question.Options?.Any(option => option.ChoiceId.ToString("D") == choice.ChoiceId) == true).ToArray();
            var ids = kept.Select(choice => Guid.Parse(choice.ChoiceId)).ToArray();
            var texts = catalog ? kept.Select(choice => new FormEntryOptionResponse(Guid.Parse(choice.ChoiceId), choice.Label, choice.Detail)).ToArray() : null;
            if (question.Type == "multiChoice") return new(question.FieldId, null, ids, texts);
            return ids.Length == 1 ? new(question.FieldId, ids[0].ToString("D"), null, texts) : null;
        }
        return value switch
        {
            string text => new(question.FieldId, text, null, null),
            bool boolean => new(question.FieldId, boolean ? "true" : "false", null, null),
            StructuredRecordLocationValue location => new(question.FieldId, FormLocationPolicy.Canonical(location), null, null),
            _ => null,
        };
    }
}
