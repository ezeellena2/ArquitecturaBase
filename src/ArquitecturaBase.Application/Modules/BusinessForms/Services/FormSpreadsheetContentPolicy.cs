using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The one rule for what a row of a read sheet holds: some text, or a cell the reader could not read (which has no text to show).
// The import counts its rows with it, the assistant's preparation shows them, and the import from the assistant's copy skips the
// first one as the header the model saw: the three have to agree.
internal static class FormSpreadsheetContentPolicy
{
    public static bool HasContent(FormSpreadsheetCell cell) => cell.Error is not null || !string.IsNullOrWhiteSpace(cell.Text);

    public static bool HasContent(FormSpreadsheetRow row) => row.Cells.Any(HasContent);
}
