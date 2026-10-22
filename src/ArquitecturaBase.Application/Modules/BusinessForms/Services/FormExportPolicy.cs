using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormExportPolicy
{
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
            "photo" or "document" when cell.Value is IReadOnlyList<StructuredRecordFileValue> files => new("text", files.Count.ToString(CultureInfo.InvariantCulture) + " · " + string.Join("; ", files.Select(file => file.Name))),
            _ => new("text", cell.Value),
        };
    }
    public static FormWorkbookCell CatalogValue(FormResponseCell cell)
    {
        if (cell.State != "Answered") return Cell(cell);
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
