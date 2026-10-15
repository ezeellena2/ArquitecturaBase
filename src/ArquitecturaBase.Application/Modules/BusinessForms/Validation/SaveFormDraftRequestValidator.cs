using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed partial class SaveFormDraftRequestValidator : AbstractValidator<SaveFormDraftRequest>
{
    public SaveFormDraftRequestValidator()
    {
        RuleFor(x => x).Custom((request, context) =>
        {
            void Fail(string path) => context.AddFailure(path, ErrorMessages.Get(FormDefinitionErrors.ContentInvalid.Code));
            if (request.RequestId == Guid.Empty) Fail("requestId");

            if (request.Prefix is null || !PrefixPattern().IsMatch(request.Prefix)) Fail("prefix");
            if (request.ExpectedRevision is < 1 or > 9007199254740991) Fail("expectedRevision");
            var draft = request.Draft;
            if (draft is null) { Fail("draft"); return; }
            if (draft.Format != 1) Fail("draft.format");
            if (draft.Culture is not ("es" or "en")) { Fail("draft.culture"); return; }
            CheckText(request.Name, draft.Culture, 120, "name", Fail, true);
            if (draft.SubmitLabel is not null) CheckText(draft.SubmitLabel, draft.Culture, 160, "draft.submitLabel", Fail);
            if (draft.Fields is null || draft.Fields.Count > 40) { Fail("draft.fields"); return; }
            var locals = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < draft.Fields.Count; i++)
            {
                var field = draft.Fields[i]; var path = $"draft.fields[{i}]";
                if (field is null) { Fail(path); continue; }
                if (field.LocalId is null || !LocalPattern().IsMatch(field.LocalId) || !locals.Add(field.LocalId)) Fail(path + ".localId");
                if (field.Type is not ("shortText" or "longText" or "decimal" or "date" or "boolean" or "choice" or "photo" or "document")) Fail(path + ".type");
                if (field.FieldId == Guid.Empty || field.ReplacesFieldId == Guid.Empty || (field.FieldId.HasValue && field.ReplacesFieldId.HasValue)) Fail(path + ".fieldId");
                CheckText(field.Label, draft.Culture, 160, path + ".label", Fail);
                if (field.Help is not null)
                    foreach (var (language, text) in field.Help)
                        if (language is not ("es" or "en") || (text is not null && (StringInfo.ParseCombiningCharacters(text).Length > 1000
                            || !IsStorableText(text, allowLineBreaks: true, requireVisible: !string.IsNullOrWhiteSpace(text))))) Fail(path + ".help." + language);
                var fileType = field.Type is "photo" or "document";
                if (fileType ? field.Files is not { MinFiles: >= 0 and <= 10, MaxFiles: >= 1 and <= 10 } files
                    || files.MinFiles > files.MaxFiles || field.Required != (files.MinFiles > 0)
                    || (field.Type == "photo" ? files.PhotoSource is not ("cameraOrGallery" or "camera") : files.PhotoSource is not null)
                    : field.Files is not null) Fail(path + ".files");
                var textType = field.Type is "shortText" or "longText";
                if ((textType && field.MaxLength is < 1 or > 10000) || (!textType && field.MaxLength.HasValue)) Fail(path + ".maxLength");
                if (field.Type != "choice")
                {
                    if (field.Options is not null) Fail(path + ".options");
                    if (field.Source is not null) Fail(path + ".source.catalog");
                    continue;
                }
                if (field.Source is not null)
                {
                    if (field.Source.Catalog is not { } catalogId || catalogId == Guid.Empty) Fail(path + ".source.catalog");
                    if (field.Options is not null) Fail(path + ".options");
                    continue;
                }
                if (field.Options is null || field.Options.Count is < 1 or > 50) { Fail(path + ".options"); continue; }
                var optionLocals = new HashSet<string>(StringComparer.Ordinal);
                var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var j = 0; j < field.Options.Count; j++)
                {
                    var option = field.Options[j]; var optionPath = path + $".options[{j}]";
                    if (option is null) { Fail(optionPath); continue; }
                    if (option.LocalId is null || !LocalPattern().IsMatch(option.LocalId) || !optionLocals.Add(option.LocalId)) Fail(optionPath + ".localId");
                    if (option.ChoiceId == Guid.Empty) Fail(optionPath + ".choiceId");
                    CheckText(option.Label, draft.Culture, 160, optionPath + ".label", Fail);
                    if (option.Label is not null && option.Label.TryGetValue(draft.Culture, out var label)
                        && label is not null && !labels.Add(label.Normalize().Trim())) Fail(optionPath + ".label." + draft.Culture);
                }
            }
        });
    }
    private static void CheckText(IReadOnlyDictionary<string, string>? texts, string culture, int limit, string path, Action<string> fail, bool utf16 = false)
    {
        if (texts is null || !texts.TryGetValue(culture, out var primary) || string.IsNullOrWhiteSpace(primary)) fail(path + "." + culture);
        if (texts is null) return;
        foreach (var (language, text) in texts)
            if (language is not ("es" or "en") || text is null || (language == culture && string.IsNullOrWhiteSpace(text))
                || (text is not null && ((utf16 ? text.Length : StringInfo.ParseCombiningCharacters(text).Length) > limit
                    || !IsStorableText(text, allowLineBreaks: false, requireVisible: !string.IsNullOrWhiteSpace(text))))) fail(path + "." + language);
    }
    private static bool IsStorableText(string text, bool allowLineBreaks, bool requireVisible)
    {
        var hasVisible = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Control)
            {
                if (allowLineBreaks && rune.Value is '\r' or '\n' or '\t') continue;
                return false;
            }
            if (category == UnicodeCategory.Format && rune.Value is not (0x200C or 0x200D)) return false;
            if (category is not (UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.SpaceSeparator
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)) hasVisible = true;
        }
        return !requireVisible || hasVisible;
    }
    [GeneratedRegex("^[A-Z][A-Z0-9]{0,11}\\z", RegexOptions.CultureInvariant)] private static partial Regex PrefixPattern();
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}\\z", RegexOptions.CultureInvariant)] private static partial Regex LocalPattern();
}
