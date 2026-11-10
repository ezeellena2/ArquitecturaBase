using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppSpreadsheetDispatchService
{
    /// <summary>
    /// Downloads, checks, prepares and copies the contact's oldest pending spreadsheet outside the transaction, and keeps what came of
    /// it: the copy and the prepared sheet, the reason it is not read (answered once) or that it is not available.
    /// </summary>
    Task<Result> PrepareAsync(Guid contactId, CancellationToken cancellationToken);
}
