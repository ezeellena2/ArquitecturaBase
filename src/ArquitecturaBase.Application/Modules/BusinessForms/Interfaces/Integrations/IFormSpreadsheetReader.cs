using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;

/// <summary>Reads bounded XLSX or UTF-8 CSV content without evaluating formulas. The caller owns the stream.</summary>
public interface IFormSpreadsheetReader
{
    Task<Result<FormSpreadsheet>> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken);
}
