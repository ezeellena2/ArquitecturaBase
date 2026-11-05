using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed partial class FormResponseReader
{
    public async Task<FormResponseAmendmentRow> FindAmendmentAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(record => record.Id == recordId)
            .Select(record => new
            {
                record.ReplacesRecordId, record.CorrectedByUserId, record.CorrectionReason, record.CreatedAtUtc,
                CorrectedByName = dbContext.Users.Where(user => user.Id == record.CorrectedByUserId).Select(user => user.DisplayName).FirstOrDefault(),
                record.ReplacedByRecordId,
                ReplacedAtUtc = dbContext.Set<BusinessFormRecord>().Where(correction => correction.Id == record.ReplacedByRecordId)
                    .Select(correction => (DateTime?)correction.CreatedAtUtc).FirstOrDefault(),
                record.VoidedByUserId, record.VoidedAtUtc, record.VoidReason,
                VoidedByName = dbContext.Users.Where(user => user.Id == record.VoidedByUserId).Select(user => user.DisplayName).FirstOrDefault(),
            }).SingleAsync(cancellationToken);
        FormResponseCorrectionResponse? correction = null;
        if (row.ReplacesRecordId is { } previousId)
        {
            // How the replaced version had ended: its state, its last decision and its first PDF, if it had them.
            var previous = await dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(record => record.Id == previousId)
                .Select(record => new
                {
                    record.State,
                    Decision = dbContext.Set<BusinessFormTask>().Where(task => task.RecordId == record.Id && task.DecidedAtUtc != null)
                        .OrderByDescending(task => task.DecidedAtUtc).ThenByDescending(task => task.Id)
                        .Select(task => new { task.DecidedAtUtc, task.DecidedByUserId,
                            Name = dbContext.Users.Where(user => user.Id == task.DecidedByUserId).Select(user => user.DisplayName).FirstOrDefault() })
                        .FirstOrDefault(),
                    PdfAtUtc = dbContext.Set<BusinessFormDocument>().Where(document => document.RecordId == record.Id)
                        .Min(document => (DateTime?)document.CreatedAtUtc),
                }).SingleAsync(cancellationToken);
            correction = new(previousId, row.CreatedAtUtc, new(row.CorrectedByUserId!.Value, row.CorrectedByName), row.CorrectionReason!,
                previous.State.ToString(), previous.Decision?.DecidedAtUtc,
                previous.Decision?.DecidedByUserId is { } decidedBy ? new(decidedBy, previous.Decision.Name) : null, previous.PdfAtUtc);
        }
        return new(correction,
            row.ReplacedByRecordId is { } replacedBy ? new(replacedBy, row.ReplacedAtUtc!.Value) : null,
            row.VoidedByUserId is { } voidedBy ? new(row.VoidedAtUtc!.Value, new(voidedBy, row.VoidedByName), row.VoidReason!) : null);
    }
}
