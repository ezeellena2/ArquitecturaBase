using System.Globalization;
using System.Text;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// The name a Flow is created with in Meta, which its official preview and WhatsApp Manager show (2026-11-06): the form's name in the
/// Flow's language (or the original one), its version and language, and the end of the publication's id, which keeps it unique in the
/// account, as Meta requires. The app later verifies the Flow and looks it up by this exact name, so it fits the 80 characters it is
/// stored with and keeps only letters, digits, spaces and common punctuation, in NFC: nothing Meta has a reason to change.
/// </summary>
internal static class WhatsAppFlowTechnicalName
{
    public const int MaxLength = 80;
    private const string Separator = " · ";
    private const string Punctuation = "-_.,;:()[]&#/+'\"!¡?¿%°@*=·";

    public static string For(IReadOnlyDictionary<string, string> names, string originalCulture, long? versionNumber, string culture,
        Guid operationId)
    {
        var tail = (versionNumber is { } number ? Separator + "v" + number.ToString(CultureInfo.InvariantCulture) : "")
            + Separator + culture + Separator + operationId.ToString("N")[^8..];
        var name = Clean(names.TryGetValue(culture, out var own) && !string.IsNullOrWhiteSpace(own) ? own
            : names.GetValueOrDefault(originalCulture) ?? "");
        if (name.Length == 0) name = "form";
        var room = MaxLength - tail.Length;
        if (name.Length > room) name = name[..(room - 1)].TrimEnd() + "…";
        return name + tail;
    }

    // Runs of anything else (spaces, emoji, symbols, invisible characters) become one space; a combining mark stays on its letter.
    private static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        var gap = false;
        foreach (var character in text.Normalize(NormalizationForm.FormC))
        {
            var category = char.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                if (!gap && builder.Length > 0) builder.Append(character);
                continue;
            }
            if (!char.IsLetterOrDigit(character) && !Punctuation.Contains(character))
            {
                gap = true;
                continue;
            }
            if (gap && builder.Length > 0) builder.Append(' ');
            builder.Append(character);
            gap = false;
        }
        return builder.ToString();
    }
}
