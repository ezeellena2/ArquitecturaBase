using System.Text;
using System.Text.RegularExpressions;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static partial class FormMissingRequestPrivacyPolicy
{
    public static bool HasContactDetails(string? name, string? description)
    {
        var text = (name ?? "") + " " + (description ?? "");
        return text.Contains('@') || LongDigits().IsMatch(text) || Url().IsMatch(text);
    }

    public static bool ContainsIdentifier(string text, string? identifier) =>
        !string.IsNullOrWhiteSpace(identifier) && identifier.Length >= 3
        && Regex.IsMatch(text.Normalize(NormalizationForm.FormC),
            @"(?<![\p{L}\p{N}\p{M}_])" + Regex.Escape(identifier.Normalize(NormalizationForm.FormC)) + @"(?![\p{L}\p{N}\p{M}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    [GeneratedRegex(@"(?:\p{Nd}[\s()+.\p{Pd}\u2212]*){7,}", RegexOptions.CultureInvariant)]
    private static partial Regex LongDigits();

    [GeneratedRegex(@"\b(?:[a-z][a-z0-9+.\-]*://|www\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Url();
}
