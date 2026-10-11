using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormCaptureReceiptReader
{
    Task<FormCaptureReceiptRow?> FindAsync(Guid sourceReceiptId, CancellationToken cancellationToken);
}
