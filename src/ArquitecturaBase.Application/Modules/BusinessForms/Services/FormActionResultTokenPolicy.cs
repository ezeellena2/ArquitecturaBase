using System.Globalization;
using System.Text;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>A datum of a result an action declares (RE1-A): its source and its key.</summary>
internal sealed record FormActionResultDatum(string Source, string Key);

/// <summary>
/// The data of a result in a message (RE1-A), written <c>{result:&lt;source&gt;.&lt;key&gt;}</c>. The text of a message is made
/// when the response arrives, before the result exists: the datum stays marked between two unit separators, which are taken out
/// of the answers the message quotes, and is filled in when the message is sent, in the language of whoever receives it.
/// </summary>
internal static class FormActionResultTokenPolicy
{
    public const string Prefix = "result:";
    private const char Marker = '\u001F';

    public static bool IsResult(string token) => token.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool TryParse(string token, out FormActionResultDatum datum)
    {
        datum = null!;
        if (!IsResult(token)) return false;
        var name = token[Prefix.Length..];
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return false;
        datum = new(name[..dot], name[(dot + 1)..]);
        return true;
    }

    public static string Mark(string token) => Marker + token + Marker;

    public static bool HasData(string body) => body.Contains(Marker, StringComparison.Ordinal);

    public static string Unmarked(string text) => text.Replace(Marker.ToString(), "", StringComparison.Ordinal);

    public static string Resolve(string body, IReadOnlyList<FormRecordResultRow> results, string culture)
    {
        if (!HasData(body)) return body;
        var stored = results.OrderByDescending(row => row.RecordedAtUtc)
            .Select(row => FormRecordResultRecorder.Read(row.Source, row.SourceNameJson, row.ValuesJson)).ToArray();
        var text = new StringBuilder();
        for (var index = 0; index < body.Length; index++)
        {
            var end = body[index] == Marker ? body.IndexOf(Marker, index + 1) : -1;
            if (end < 0) { text.Append(body[index]); continue; }
            if (TryParse(body[(index + 1)..end], out var datum)
                && stored.FirstOrDefault(result => result.Source == datum.Source)?.Values.FirstOrDefault(value => value.Key == datum.Key) is { } value)
                text.Append(Format(value, culture));
            index = end;
        }
        return text.ToString();
    }

    private static string Format(StructuredRecordResultValue value, string culture) => value.Value is not { } raw ? "" : value.Kind switch
    {
        // A date is written with numbers only, day first in Spanish and month first in English (never the month as a word).
        StructuredRecordResultField.Date => DateOnly.ParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture),
        StructuredRecordResultField.Number => decimal.Parse(raw, CultureInfo.InvariantCulture).ToString("0.############################", CultureInfo.GetCultureInfo(culture)),
        _ => raw,
    };
}
