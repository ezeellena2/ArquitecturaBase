using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static partial class FormMissingRequestPrivacyPolicy
{
    public static bool HasContactDetails(string? name, string? description)
    {
        var text = ((name ?? "") + " " + (description ?? "")).Normalize(NormalizationForm.FormKC);
        if (text.Contains('@') || Url().IsMatch(text)) return true;
        foreach (Match run in NumericRun().Matches(text))
        {
            if (run.Value.Count(char.IsDigit) < 7) continue;
            if (run.Value.Split(',', StringSplitOptions.TrimEntries).All(value =>
                DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))) continue;
            if (IsExplicitAmount(text, run)) continue;
            return true;
        }
        return false;
    }

    public static bool HasIdentifier(string? name, string? description, UserAccount user)
    {
        var text = (name ?? "") + " " + (description ?? "");
        return new[] { user.Email, user.PhoneNumber, user.DisplayName }.Any(value => ContainsIdentifier(text, value));
    }

    public static bool ContainsIdentifier(string text, string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return false;
        var known = WithoutDiacritics(identifier);
        return known.Length >= 3 && Regex.IsMatch(WithoutDiacritics(text),
            @"(?<![\p{L}\p{N}\p{M}_])" + Regex.Escape(known) + @"(?![\p{L}\p{N}\p{M}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static string WithoutDiacritics(string text) => string.Concat(text.Normalize(NormalizationForm.FormKC)
        .Normalize(NormalizationForm.FormD).Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));

    private static bool IsExplicitAmount(string text, Match run)
    {
        if (!Amount().IsMatch(run.Value)
            || !CurrencyBefore().IsMatch(text[..run.Index]) && !CurrencyAfter().IsMatch(text[(run.Index + run.Length)..])) return false;
        return decimal.TryParse(run.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
            || decimal.TryParse(run.Value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-AR"), out _);
    }

    [GeneratedRegex(@"\p{Nd}(?:[\p{Nd}\s()+.,\p{Pd}\u2212]*\p{Nd})?", RegexOptions.CultureInvariant)]
    private static partial Regex NumericRun();

    [GeneratedRegex(@"^(?:[0-9]+(?:[.,][0-9]{1,2})?|[0-9]{1,3}(?:\.[0-9]{3})+(?:,[0-9]{1,2})?|[0-9]{1,3}(?:,[0-9]{3})+(?:\.[0-9]{1,2})?)$", RegexOptions.CultureInvariant)]
    private static partial Regex Amount();

    [GeneratedRegex(@"(?:\b(?:ARS|USD|EUR)|[$€])\s*[+\-\u2212]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyBefore();

    [GeneratedRegex(@"^\s*(?:ARS\b|USD\b|EUR\b|[$€])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyAfter();

    [GeneratedRegex(@"\b(?:[a-z][a-z0-9+.\-]*://|www\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Url();
}
