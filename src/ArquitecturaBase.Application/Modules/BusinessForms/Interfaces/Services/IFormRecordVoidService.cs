using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>Voids a response with a reason (CW11-A): it keeps its number and history, stops counting and closes its approval.</summary>
public interface IFormRecordVoidService
{
    Task<Result> VoidAsync(Guid definitionId, Guid recordId, VoidFormRecordRequest request, CancellationToken cancellationToken);
}
