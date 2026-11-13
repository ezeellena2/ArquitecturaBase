using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The numbers a received document is searched by (decision 16), kept in clear beside its protected reading, each only with its digits,
/// space-separated, in order and once, within <see cref="FormReceivedDocument.MaxSearchTextLength"/>. A document with a form keeps only
/// the typed values its proposal marked searchable (amounts, an account or CUIT that passes its validation and the standard's operation
/// number); one without a form keeps only the amounts and the CUIT, CBU or CVU whose check digits pass that its reading shows. Never a
/// run of 13 to 19 digits that passes Luhn (a card), and digit groups are never joined across spaces (a phone). A search is the digits of
/// what was typed, matched as the beginning of one of those numbers.
/// </summary>
internal static partial class FormReceivedDocumentSearchPolicy
{
    public const int MinimumDigits = 3;
    private const int MinimumNumberDigits = 4;

    public static string Build(StructuredDocumentReading? reading, FormDocumentPreparedProposal? proposal) =>
        Build(proposal, reading?.Text is { Length: > 0 } text ? [(text, false)] : []);

    /// <summary>
    /// VC1-A: a row's, from its cells instead of a reading. Without a form, each cell like a reading (its amounts and its CUIT, CBU or CVU),
    /// and a number the spreadsheet typed (its canonical value) as an amount, with and without its cents.
    /// </summary>
    public static string BuildRow(FormDocumentRowReading reading, FormDocumentPreparedProposal? proposal)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return Build(proposal, [.. reading.Cells.Where(cell => !string.IsNullOrWhiteSpace(cell.Text)).Select(cell => (cell.Text!.Trim(), cell.Canonical))]);
    }

    private static string Build(FormDocumentPreparedProposal? proposal, IReadOnlyList<(string Text, bool Canonical)> texts)
    {
        var numbers = new List<string>();
        if (proposal is { Candidates.Count: > 0 })
        {
            foreach (var value in proposal.Candidates.SelectMany(candidate => candidate.Values).Where(value => value is { Searchable: true, Value: not null }))
            {
                if (value.Type == "decimal")
                {
                    numbers.Add(Digits(value.Value!));
                    // An amount is found with or without its cents: «150000.50» by «150000» too.
                    if (value.Value!.Split('.') is [var whole, _]) numbers.Add(Digits(whole));
                }
                else
                    foreach (var group in value.Value!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        if (Digits(group) is { Length: >= MinimumNumberDigits } digits) numbers.Add(digits);
            }
        }
        else
            foreach (var (read, canonical) in texts)
            {
                if (canonical && decimal.TryParse(read, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
                {
                    numbers.Add(Digits(read));
                    if (read.Split('.') is [var whole, _]) numbers.Add(Digits(whole));
                    continue;
                }
                foreach (Match match in AmountPattern().Matches(read))
                    numbers.Add(Digits(match.Groups["whole"].Value));
                foreach (Match match in NumberPattern().Matches(read))
                    if (CuitNumber.Normalize(match.Value) is not null || BankAccountKey.Parse(match.Value) is { Kind: not BankAccountKind.Alias })
                        numbers.Add(Digits(match.Value));
            }
        var builder = new StringBuilder();
        foreach (var number in numbers.Where(number => number.Length > 0 && !IsCard(number)).Distinct(StringComparer.Ordinal))
        {
            if (builder.Length + number.Length + 1 > FormReceivedDocument.MaxSearchTextLength) break;
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(number);
        }
        return builder.ToString();
    }

    /// <summary>The digits of a search, or null when it has too few to look for.</summary>
    public static string? Search(string? search)
    {
        var digits = search is null ? "" : Digits(search);
        return digits.Length >= MinimumDigits ? digits[..Math.Min(digits.Length, 64)] : null;
    }

    private static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    // A card number: 13 to 19 digits whose Luhn check passes.
    private static bool IsCard(string digits)
    {
        if (digits.Length is < 13 or > 19) return false;
        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
        {
            var digit = digits[digits.Length - 1 - index] - '0';
            if (index % 2 == 1 && (digit *= 2) > 9) digit -= 9;
            sum += digit;
        }
        return sum % 10 == 0;
    }

    // An amount as a document writes it: after a currency sign, or with its thousands and cents («150.000,00», «150,000.00»); its whole part.
    [GeneratedRegex(@"(?:(?:US\$|U\$S|\$|ARS|USD)\s?(?<whole>\d{1,3}(?:[.,]\d{3})+|\d+)(?:[.,]\d{1,2})?(?!\d))|(?<!\d)(?<whole>\d{1,3}(?:\.\d{3})+),\d{2}(?!\d)|(?<!\d)(?<whole>\d{1,3}(?:,\d{3})+)\.\d{2}(?!\d)",
        RegexOptions.CultureInvariant, 200)]
    private static partial Regex AmountPattern();

    // A CUIT, CBU or CVU as a document writes it: digits with at most one dot or dash between two of them, never a space.
    [GeneratedRegex(@"(?<!\d)\d(?:[.\-]?\d){10,30}(?!\d)", RegexOptions.CultureInvariant, 200)]
    private static partial Regex NumberPattern();
}
