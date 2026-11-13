using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;

/// <summary>Reads bounded XLSX or UTF-8 CSV content without evaluating formulas. The caller owns the stream.</summary>
public interface IFormSpreadsheetReader
{
    /// <summary>Decides by the extension of <paramref name="fileName"/>, as the import does.</summary>
    Task<Result<FormSpreadsheet>> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken);

    /// <summary>What the bytes are, never the name: a workbook by its OOXML manifest, an OLE2 container or UTF-8 text.</summary>
    FormSpreadsheetFormat DetectFormat(byte[] content);

    /// <summary>The bytes as <see cref="DetectFormat"/> said (Xlsx or Csv), with the same limits, without copying them; a CSV has no sheet name.</summary>
    Result<FormSpreadsheet> Read(byte[] content, FormSpreadsheetFormat format, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="Read"/>, every worksheet in the workbook's order, each with its name and its rows (a CSV is one sheet without a name):
    /// the rows of a spreadsheet to load (VC1-A).
    /// </summary>
    Result<IReadOnlyList<FormSpreadsheet>> ReadSheets(byte[] content, FormSpreadsheetFormat format, CancellationToken cancellationToken);
}
