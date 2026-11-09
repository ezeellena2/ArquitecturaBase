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
}
