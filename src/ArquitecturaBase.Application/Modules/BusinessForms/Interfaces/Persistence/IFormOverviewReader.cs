using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormOverviewReader
{
    Task<FormOverviewResponse> FindPageAsync(Guid actorUserId, ListFormOverviewRequest request, string culture,
        bool canReadRecords, bool canDecide, CancellationToken cancellationToken);
}