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
}
