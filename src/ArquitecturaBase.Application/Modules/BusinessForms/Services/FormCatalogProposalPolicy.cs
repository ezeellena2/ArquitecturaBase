using System.Globalization;
using System.Text;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogProposalPolicy
{
    public static Result Validate(FormCatalogProposalPayload proposal, FormCatalogMetadataPayload metadata, string culture)
    {
        if (culture is not ("es" or "en") || metadata.Source != "users") return FormCatalogErrors.ContentInvalid;
        var content = ValidateContent(proposal);
        if (content.IsFailure) return content.Error;
        var roles = metadata.Columns.FirstOrDefault(column => column.Key == "role")?.Choices;
        return ValidateRoles(proposal.Filters, roles ?? []);
    }

    public static Result ValidateRoles(FormCatalogFilterDefinition filters, IReadOnlyList<FormCatalogFilterChoiceResponse> roles)
    {
        foreach (var condition in filters.Conditions!)
            if (condition!.Column == "role" && !roles.Any(role => Guid.TryParse(role.Key, out var known)
                && Guid.TryParse(condition.Value, out var selected) && known == selected))
                return FieldErrors.On(FormCatalogErrors.ContentInvalid, "filters.conditions");
        return Result.Success();
    }

    public static Result ValidateContent(FormCatalogProposalPayload proposal) => proposal.MetadataReadOrdinal < 1
        ? FormCatalogErrors.ContentInvalid
        : ValidateContent(new FormCatalogProposalArguments(proposal.Name, proposal.Description, proposal.Filters, proposal.Limitations));

    public static Result ValidateContent(FormCatalogProposalArguments proposal)
    {
        if (
            !ValidText(proposal.Name, 120, false) || !ValidText(proposal.Description, 1000, true) ||
            proposal.Filters is null || proposal.Filters.Conditions is not { Count: <= 10 } ||
            proposal.Limitations is not { Count: <= 5 }) return FormCatalogErrors.ContentInvalid;
        var invalid = false;
        FormCatalogFilterRequestValidation.Validate(proposal.Filters, (_, _) => invalid = true);
        if (invalid) return FormCatalogErrors.ContentInvalid;
        foreach (var condition in proposal.Filters.Conditions)
        {
            if (condition?.Value is not { } value || !ValidText(value, 256, false)) return FormCatalogErrors.ContentInvalid;
        }
        if (proposal.Limitations.Any(limitation => limitation is null || limitation.Code != "UnsupportedColumn" ||
            !ValidText(limitation.Criterion, 80, false))) return FormCatalogErrors.ContentInvalid;
        return Result.Success();
    }

    public static Result ValidateEdits(IReadOnlyDictionary<string,string?>? name, IReadOnlyDictionary<string,string?>? description,
        FormCatalogFilterDefinition? filters, string originalCulture)
    {
        foreach (var (texts, path, limit, multiline) in new[] { (name, "name", 120, false), (description, "description", 1000, true) })
        {
            if (texts is null || !texts.TryGetValue(originalCulture, out var original) || !ValidText(original, limit, multiline))
                return FieldErrors.On(FormCatalogErrors.ContentInvalid, path + "." + originalCulture);
            foreach (var (culture, value) in texts)
                if (culture is not ("es" or "en") || !string.IsNullOrWhiteSpace(value) && !ValidText(value, limit, multiline))
                    return FieldErrors.On(FormCatalogErrors.ContentInvalid, path + "." + culture);
        }
        string? first = null;
        FormCatalogFilterRequestValidation.Validate(filters, (path, _) => first ??= path);
        if (first is not null) return FieldErrors.On(FormCatalogErrors.ContentInvalid, first);
        var content = new FormCatalogProposalArguments(name![originalCulture]!, description![originalCulture]!, filters!, []);
        return ValidateContent(content);
    }

    public static Dictionary<string,string> Texts(IReadOnlyDictionary<string,string?> source) => source
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);

    private static bool ValidText(string? value, int maximum, bool multiline)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || !StorableText.IsStorable(value)) return false;
        var visible = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Control)
            {
                if (multiline && rune.Value is '\r' or '\n' or '\t') continue;
                return false;
            }
            if (category == UnicodeCategory.Format && rune.Value is not (0x200C or 0x200D)) return false;
            if (category is not (UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.SpaceSeparator
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)) visible = true;
        }
        return visible;
    }
}
