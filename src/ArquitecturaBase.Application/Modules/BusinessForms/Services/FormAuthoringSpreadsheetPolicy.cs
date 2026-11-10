using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// Stage 2: a proposal names the columns of the turn's spreadsheet by their letter, against the sheet the engine prepared (the
// file is never opened here). The code and the option are required, and the value only when the value type is not text. Each
// letter has to be one of the sheet's columns: the code and the option may share one (a simple list), and the value, the detail
// and the fields never repeat a column. The card shows each mapped column, ordered as the catalog, with the text of the first row
// with content when it holds headers (the prepared sheet skips empty rows, so it may not be the sheet's row 1), and the first
// three rows after it; the rows of data are the ones with content but that one.
internal static partial class FormAuthoringSpreadsheetPolicy
{
    public const int SampleRows = 3;
    public const int MaximumColumns = 4 + BusinessFormCatalog.MaxFields;
    private static readonly string[] Targets = ["code", "option", "value", "detail", "field"];

    // A column a mapping names: what it fills (for a field, which one and, when the catalog exists, its key), the path of its
    // argument and its letter as written, null when nothing fills it.
    public sealed record Column(string Target, string Path, string? Letter, string? Field = null, string? Key = null);

    public sealed record Mapping(IReadOnlyList<FormAuthoringIssue> Issues, IReadOnlyList<Column> Columns);

    // A blank letter fills nothing; what fills something is compared by its letter in capitals.
    public static string? Given(string? letter) => string.IsNullOrWhiteSpace(letter) ? null : letter.Trim().ToUpperInvariant();

    // The columns come in the catalog's order: code, option, value, detail and then the fields. Prefix is where the mapping's
    // arguments are ("mapping." for a proposed catalog, nothing for an import).
    public static Mapping Map(StructuredSpreadsheet sheet, string prefix, string? code, string? option, string? value, string? detail,
        string valueType, IEnumerable<Column> fields)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var issues = new List<FormAuthoringIssue>();
        (code, option, value, detail) = (Given(code), Given(option), Given(value), Given(detail));
        if (code is null) issues.Add(new(prefix + "code", "required"));
        if (option is null) issues.Add(new(prefix + "option", "required"));
        if (valueType is "integer" or "decimal" or "date" && value is null) issues.Add(new(prefix + "value", "required"));
        if (valueType == "text" && value is not null) issues.Add(new(prefix + "value", "invalid"));
        // A value the type does not take is already rejected (or the type is): it is not a column to check.
        if (valueType is not ("integer" or "decimal" or "date")) value = null;
        Column[] columns = [new("code", prefix + "code", code), new("option", prefix + "option", option), new("value", prefix + "value", value),
            new("detail", prefix + "detail", detail), .. fields.Select(field => field with { Letter = Given(field.Letter) })];
        var used = new HashSet<int>();
        int? codeColumn = null;
        var mapped = new List<Column>();
        foreach (var column in columns.Where(column => column.Letter is not null))
        {
            if (Number(column.Letter!) is not { } number) { issues.Add(new(column.Path, "invalid")); continue; }
            if (number > sheet.ColumnCount) { issues.Add(new(column.Path, "outOfRange")); continue; }
            mapped.Add(column);
            if (column.Target == "code") codeColumn = number;
            if (column.Target == "option" && number == codeColumn) continue;
            if (!used.Add(number)) issues.Add(new(column.Path, "repeated"));
        }
        return new(issues, mapped);
    }

    // The card: the file, the sheet and its rows of data, each mapped column with its header and up to three rows after them.
    public static FormAuthoringSpreadsheetSummary Summary(StructuredSpreadsheet sheet, bool hasHeaders, IReadOnlyList<Column> columns)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var header = hasHeaders && sheet.Rows.Count > 0 ? sheet.Rows[0] : null;
        return new(sheet.FileName, sheet.SheetName, Math.Max(0, sheet.RowCount - (hasHeaders ? 1 : 0)), hasHeaders,
            columns.Select(column => new FormAuthoringMappedColumn(column.Target, column.Field, column.Key, column.Letter!,
                header?.Cells.GetValueOrDefault(column.Letter!))).ToArray(),
            sheet.Rows.Skip(hasHeaders ? 1 : 0).Take(SampleRows)
                .Select(row => (IReadOnlyList<string>)columns.Select(column => row.Cells.GetValueOrDefault(column.Letter!) ?? "").ToArray()).ToArray());
    }

    // What a stored proposal says it took, read back: the catalog's order, letters of the import's columns that never repeat but
    // for the code and the option, a value only when the type takes one, headers only with headers and one text per column in each
    // of up to three rows. A proposed catalog names its own fields, in their order and without keys; an import, keyed fields.
    public static bool IsValid(FormAuthoringSpreadsheetSummary summary, string valueType, IReadOnlyList<string>? proposedFields)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary is not { FileName.Length: > 0 and <= 120, SheetName.Length: <= 120, RowCount: >= 0, Columns: { Count: >= 2 and <= MaximumColumns } columns,
            Sample: { Count: <= SampleRows } sample } || columns.Any(column => column is not { Target: not null, Letter: not null } ||
                !Targets.Contains(column.Target, StringComparer.Ordinal) || Number(column.Letter) is not (>= 1 and <= FormCatalogImportLimits.MaximumColumns) ||
                column.Header is { Length: > 120 } || !summary.HasHeaders && column.Header is not null))
            return false;
        var order = string.Concat(columns.Select(column => column.Target switch { "code" => 'c', "option" => 'o', "value" => 'v', "detail" => 'd', _ => 'f' }));
        if (!TargetOrder().IsMatch(order) || order.Contains('v', StringComparison.Ordinal) != (valueType is "integer" or "decimal" or "date")) return false;
        var others = columns.Skip(2).Select(column => column.Letter).ToArray();
        if (others.Distinct(StringComparer.Ordinal).Count() != others.Length || others.Any(letter => letter == columns[0].Letter || letter == columns[1].Letter))
            return false;
        var fields = columns.Where(column => column.Target == "field").ToArray();
        if (columns.Any(column => column.Target != "field" && (column.Field is not null || column.Key is not null)) ||
            fields.Any(field => field.Field is not { Length: > 0 and <= 120 })) return false;
        if (proposedFields is null)
        {
            if (fields.Any(field => field.Key is null || !FieldKey().IsMatch(field.Key)) ||
                fields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Count() != fields.Length) return false;
        }
        else
        {
            var positions = fields.Select(field => field.Key is null ? IndexOf(proposedFields, field.Field!) : -1).ToArray();
            if (positions.Any(position => position < 0) || positions.Zip(positions.Skip(1)).Any(pair => pair.First >= pair.Second)) return false;
        }
        return sample.All(row => row is not null && row.Count == columns.Count && row.All(cell => cell is not null));
    }

    // A, B… Z, AA… as a spreadsheet numbers its columns; anything else is not a column.
    public static int? Number(string letter)
    {
        if (letter.Length is 0 or > 3 || letter.Any(character => character is < 'A' or > 'Z')) return null;
        return letter.Aggregate(0, (number, character) => number * 26 + (character - 'A' + 1));
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var index = 0; index < names.Count; index++) if (string.Equals(names[index], name, StringComparison.Ordinal)) return index;
        return -1;
    }

    [GeneratedRegex("^co(v)?(d)?f*$")]
    private static partial Regex TargetOrder();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex FieldKey();
}
