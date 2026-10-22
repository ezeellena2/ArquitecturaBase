using System.Text;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Import;

internal static class FormCsvReader
{
    private const int MaximumRecords = BusinessFormCatalog.MaxItems + 2;

    public static Result<FormSpreadsheet> Read(byte[] bytes, string name, string sha, CancellationToken ct)
    {
        string text;
        try
        {
            var bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            text = new UTF8Encoding(false, true).GetString(bytes.AsSpan(bom));
        }
        catch (DecoderFallbackException) { return FormCatalogImportErrors.FileUnreadable; }
        if (text.Contains('\0', StringComparison.Ordinal)) return FormCatalogImportErrors.FileUnreadable;
        var delimiter = Delimiter(text);
        var rows = new List<FormSpreadsheetRow>();
        var cells = new List<FormSpreadsheetCell>();
        var field = new StringBuilder();
        var quoted = false; var closed = false; var started = false;
        var recordNumber = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if ((index & 1023) == 0) ct.ThrowIfCancellationRequested();
            var character = text[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; }
                    else { quoted = false; closed = true; }
                }
                else field.Append(character);
                continue;
            }
            if (character == delimiter || character is '\r' or '\n')
            {
                if (cells.Count == FormCatalogImportLimits.MaximumColumns) return FormCatalogImportErrors.TooManyColumns;
                cells.Add(new(field.ToString())); field.Clear(); started = false; closed = false;
                if (character != delimiter)
                {
                    if (recordNumber == 1 || cells.Any(cell => !string.IsNullOrWhiteSpace(cell.Text)))
                    {
                        if (rows.Count == MaximumRecords) return FormCatalogImportErrors.TooManyRows;
                        rows.Add(new(recordNumber, cells.ToArray()));
                    }
                    recordNumber++; cells.Clear();
                    if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                }
            }
            else if (character == '"' && !started && !closed) { quoted = true; started = true; }
            else
            {
                if (closed || character == '"') return FormCatalogImportErrors.FileUnreadable;
                field.Append(character); started = true;
            }
        }
        if (quoted) return FormCatalogImportErrors.FileUnreadable;
        if (started || closed || cells.Count > 0)
        {
            if (cells.Count == FormCatalogImportLimits.MaximumColumns) return FormCatalogImportErrors.TooManyColumns;
            cells.Add(new(field.ToString()));
            if (recordNumber == 1 || cells.Any(cell => !string.IsNullOrWhiteSpace(cell.Text)))
            {
                if (rows.Count == MaximumRecords) return FormCatalogImportErrors.TooManyRows;
                rows.Add(new(recordNumber, cells.ToArray()));
            }
        }
        return new FormSpreadsheet(string.IsNullOrEmpty(name) ? "CSV" : name, sha, rows);
    }

    private static char Delimiter(string text)
    {
        var quoted = false;
        var comma = 0; var semicolon = 0; var tab = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"') index++;
                else quoted = !quoted;
            }
            else if (!quoted)
            {
                if (character is '\r' or '\n') break;
                if (character == ',') comma++;
                else if (character == ';') semicolon++;
                else if (character == '\t') tab++;
            }
        }
        return semicolon > comma && semicolon >= tab ? ';' : tab > comma ? '\t' : ',';
    }
}
