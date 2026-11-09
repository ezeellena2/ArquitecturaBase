using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormExportPolicy
{
    /// <summary>A datum of a result (RE1-A) as an Excel cell of its kind; empty without a value.</summary>
    public static FormWorkbookCell ResultCell(string kind, string? value) => value is null ? new("text", null) : kind switch
    {
        StructuredRecordResultField.Date => new("date", DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue)),
        StructuredRecordResultField.Number => new("decimal", decimal.Parse(value, CultureInfo.InvariantCulture)),
        _ => new("text", value),
    };

    /// <summary>
    /// The name of the sheet of a block: its own, without what Excel rejects, up to 31 characters without splitting a character
    /// and without an apostrophe or a space at either end; «Ítems» if it ends empty, like the responses' sheet or like the one
    /// Excel keeps («History»), and « (2)» if another sheet has it already. Excel compares sheet names without case.
    /// </summary>
    public static string SheetName(string label, IReadOnlyCollection<string> taken, string culture)
    {
        var name = Clean(Cut(Clean(new string(label.Where(character => character is not (':' or '\\' or '/' or '?' or '*' or '[' or ']')).ToArray())), 31));
        if (name.Length == 0 || name.Equals(FormResponseTexts.Get("Sheet", culture), StringComparison.OrdinalIgnoreCase)
            || name.Equals("History", StringComparison.OrdinalIgnoreCase)) name = FormResponseTexts.Get("Items", culture);
        var candidate = name;
        for (var copy = 2; taken.Contains(candidate, StringComparer.OrdinalIgnoreCase); copy++)
        {
            var suffix = " (" + copy.ToString(CultureInfo.InvariantCulture) + ")";
            candidate = Clean(Cut(name, 31 - suffix.Length)) + suffix;
        }
        return candidate;
    }

    private static string Clean(string text)
    {
        string previous;
        do
        {
            previous = text;
            text = text.Trim().Trim('\'');
        }
        while (text != previous);
        return text;
    }

    // Up to the given length in UTF-16 units, without splitting a character such as an emoji.
    private static string Cut(string text, int length)
    {
        var end = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext() && elements.ElementIndex + elements.GetTextElement().Length <= length)
            end = elements.ElementIndex + elements.GetTextElement().Length;
        return text[..end];
    }
    public static FormWorkbookCell Cell(FormResponseCell cell)
    {
        if (cell.State == "NotAsked") return new("text", "—");
        if (cell.State == "Unanswered") return new("text", null);
        return cell.Type switch
        {
            "decimal" => new("decimal", decimal.Parse((string)cell.Value!, CultureInfo.InvariantCulture)),
            "date" => new("date", DateOnly.ParseExact((string)cell.Value!, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue)),
            "boolean" => new("boolean", cell.Value),
            "choice" when cell.Value is StructuredRecordChoiceValue choice => new("text", choice.Label + (string.IsNullOrEmpty(choice.Detail) ? "" : " · " + choice.Detail)),
            "multiChoice" when cell.Value is IReadOnlyList<StructuredRecordChoiceValue> choices => new("text", string.Join("; ", choices.Select(choice => choice.Label))),
            "photo" or "document" when cell.Value is IReadOnlyList<StructuredRecordFileValue> files => new("text", files.Count.ToString(CultureInfo.InvariantCulture) + " · " + string.Join("; ", files.Select(file => file.Name))),
            "group" when cell.Value is IReadOnlyCollection<FormResponseRowCells> rows => new("integer", (long)rows.Count),
            _ => new("text", cell.Value),
        };
    }
    public static FormWorkbookCell CatalogField(FormResponseCell cell, Guid fieldId)
    {
        if (cell.State != "Answered") return Cell(cell);
        if (cell.Type == "multiChoice")
        {
            var choices = (IReadOnlyList<StructuredRecordChoiceValue>)cell.Value!;
            return new("text", choices.Count == 0 ? null : string.Join("; ", choices.Select(choice =>
            {
                var value = choice.Fields?.SingleOrDefault(field => field.FieldId == fieldId);
                return value is null ? "—" : value.Type == "catalog" ? value.Text : value.Value;
            })));
        }
        var field = ((StructuredRecordChoiceValue)cell.Value!).Fields?.SingleOrDefault(field => field.FieldId == fieldId);
        if (field is null) return new("text", "—");
        return field.Type switch
        {
            "integer" => new("integer", long.Parse(field.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
            "decimal" => new("decimal", decimal.Parse(field.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)),
            "date" => new("date", DateOnly.ParseExact(field.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue)),
            "boolean" => new("boolean", bool.Parse(field.Value)),
            "catalog" => new("text", field.Text),
            "text" => new("text", field.Value),
            _ => throw new InvalidOperationException("The captured catalog field type is invalid."),
        };
    }

    public static FormWorkbookCell CatalogValue(FormResponseCell cell)
    {
        if (cell.State != "Answered") return Cell(cell);
        if (cell.Type == "multiChoice")
        {
            var values = ((IReadOnlyList<StructuredRecordChoiceValue>)cell.Value!).Select(choice => choice.Value).Where(value => value is not null).ToArray();
            return new("text", values.Length == 0 ? null : string.Join("; ", values));
        }
        var choice = (StructuredRecordChoiceValue)cell.Value!;
        if (choice.Value is null) return new("text", null);
        return choice.ValueType switch
        {
            "Integer" => new("integer", long.Parse(choice.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
            "Decimal" => new("decimal", decimal.Parse(choice.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)),
            "Date" => new("date", DateOnly.ParseExact(choice.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue)),
            _ => throw new InvalidOperationException("The captured catalog value type is invalid."),
        };
    }
}
