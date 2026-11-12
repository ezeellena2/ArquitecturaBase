using System.Text.RegularExpressions;
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// ADR 0021, point 5: what the server checks in a drafted reply before it is queued (and the capability before it proposes it).
/// Not empty, within the agent's maximum length, in the language of the conversation, and without links, phones, emails or
/// prices that are not in what the business loaded (its knowledge and profile). It checks form and provenance, not meaning.
/// </summary>
internal static partial class WhatsAppDraftedReplyPolicy
{
    private static readonly HashSet<string> SpanishWords = new(StringComparer.Ordinal)
    {
        "el", "la", "los", "las", "de", "del", "que", "y", "en", "por", "para", "con", "una", "uno", "es", "son", "está", "están", "no",
        "sí", "se", "te", "tu", "nos", "al", "lo", "como", "pero", "más", "hay", "podés", "querés", "tenés", "hacemos", "enviamos",
    };

    private static readonly HashSet<string> EnglishWords = new(StringComparer.Ordinal)
    {
        "the", "and", "to", "of", "for", "with", "is", "are", "you", "we", "our", "your", "it", "this", "that", "by", "can", "will", "be",
        "have", "has", "do", "does", "not", "yes", "ship", "there",
    };

    /// <summary>The problems of <paramref name="text"/>: empty, tooLong, invalid, email, link, phone, price or language.</summary>
    public static IReadOnlyList<string> Problems(string? text, string culture, int maximumLength, IEnumerable<string> loaded)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0) return ["empty"];
        var problems = new List<string>();
        if (value.Length > maximumLength) problems.Add("tooLong");
        if (!StorableText.IsStorable(value) || value.Any(character => char.IsControl(character) && character is not '\n')) problems.Add("invalid");
        var source = string.Join("\n", loaded.Where(item => !string.IsNullOrWhiteSpace(item)));
        var lowerSource = source.ToLowerInvariant();
        var emails = EmailPattern().Matches(value).Select(match => Trim(match.Value)).ToArray();
        if (emails.Any(email => !lowerSource.Contains(email.ToLowerInvariant(), StringComparison.Ordinal))) problems.Add("email");
        var withoutEmails = EmailPattern().Replace(value, " ");
        if (LinkPattern().Matches(withoutEmails).Select(match => Trim(match.Value))
            .Any(link => !lowerSource.Contains(link.ToLowerInvariant(), StringComparison.Ordinal))) problems.Add("link");
        var loadedPhones = Phones(source).ToHashSet(StringComparer.Ordinal);
        if (Phones(withoutEmails).Any(phone => !loadedPhones.Contains(phone))) problems.Add("phone");
        var loadedPrices = Prices(source).ToHashSet(StringComparer.Ordinal);
        if (Prices(value).Any(price => !loadedPrices.Contains(price))) problems.Add("price");
        if (WrongLanguage(value, culture)) problems.Add("language");
        return problems;
    }

    private static IEnumerable<string> Phones(string text) => PhonePattern().Matches(text)
        .Select(match => string.Concat(match.Value.Where(char.IsAsciiDigit))).Where(digits => digits.Length >= 7);

    private static IEnumerable<string> Prices(string text) => PricePattern().Matches(text)
        .Select(match => string.Concat(match.Value.Where(char.IsAsciiDigit))).Where(digits => digits.Length > 0);

    private static bool WrongLanguage(string text, string culture)
    {
        var words = WordPattern().Matches(text.ToLowerInvariant()).Select(match => match.Value).ToArray();
        var spanish = words.Count(SpanishWords.Contains) + text.Count(character => character is '¿' or '¡' or 'ñ' or 'Ñ');
        var english = words.Count(EnglishWords.Contains);
        return culture == "en" ? text.IndexOfAny(['¿', '¡']) >= 0 || (spanish >= 3 && spanish > english * 2) : english >= 3 && english > spanish * 2;
    }

    private static string Trim(string value) => value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '»', '"', '\'');

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}-]+(?:\.[\p{L}\p{N}-]+)*\.\p{L}{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?i)(?:https?://\S+|www\.\S+|\b[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*\.(?:com|net|org|ar|io|app|info|me|co|ly|gob|gov|edu|biz|online|shop|store)\b(?:/\S*)?)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\+?\(?\d[\d\s().-]{5,}\d")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"(?i)(?:\$|u\$s|us\$|usd|ars|€)\s?\d[\d.,]*|\d[\d.,]*\s?(?:pesos|d[oó]lares|usd|ars|euros)\b")]
    private static partial Regex PricePattern();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordPattern();
}
