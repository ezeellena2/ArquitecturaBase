using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormOverviewReader
{
    /// <summary>
    /// Published forms with the dimensions the actor may see: responses with <paramref name="canReadRecords"/>, pending
    /// approvals with <paramref name="canDecide"/>. Reading or completing (<paramref name="canCreate"/>) lists every form;
    /// deciding alone, only those with something to decide.
    /// </summary>
    Task<FormOverviewResponse> FindPageAsync(Guid actorUserId, ListFormOverviewRequest request, string culture,
        bool canReadRecords, bool canDecide, bool canCreate, CancellationToken cancellationToken);
}