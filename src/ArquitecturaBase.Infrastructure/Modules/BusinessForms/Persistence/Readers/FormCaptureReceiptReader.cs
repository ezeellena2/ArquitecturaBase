using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormCaptureReceiptReader(ApplicationDbContext dbContext) : IFormCaptureReceiptReader
{
    public Task<FormCaptureReceiptRow?> FindAsync(Guid sourceReceiptId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCaptureReceipt>().AsNoTracking()
            .Where(receipt => receipt.SourceReceiptId == sourceReceiptId)
            .Select(receipt => new FormCaptureReceiptRow(receipt.SourceReceiptId, receipt.DefinitionId,
                receipt.VersionId, receipt.Fingerprint, receipt.RecordId, receipt.Number, receipt.State, receipt.ErrorCode))
            .SingleOrDefaultAsync(cancellationToken);
}
