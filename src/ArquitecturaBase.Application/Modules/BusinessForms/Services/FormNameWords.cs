using System.Globalization;
using System.Text;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The words of a name, to compare it with another (O45 and O55): without accents, case or punctuation, and without the words the
/// caller says never tell two names apart.
/// </summary>
internal static class FormNameWords
{
    public static HashSet<string> Of(string text, IReadOnlySet<string> ignored)
    {
        var plain = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                plain.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        return plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !ignored.Contains(word)).ToHashSet(StringComparer.Ordinal);
    }
}
