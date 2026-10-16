using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormPdfActionService
{
    Task<Result<FormPdfActionSnapshotResponse>> PrepareAsync(Guid runId, FormActionRecordSnapshot record, CancellationToken cancellationToken);
    Task<Result<FormPdfActionOutcomeResponse>> ExecuteAsync(FormPdfActionSnapshotResponse snapshot, bool reconciliationOnly, CancellationToken cancellationToken);
}
