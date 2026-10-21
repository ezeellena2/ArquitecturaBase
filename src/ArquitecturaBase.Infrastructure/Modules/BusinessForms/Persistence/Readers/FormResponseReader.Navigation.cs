using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormResponseReader
{
    public async Task<FormRecordNavigationRow?> FindRecordNavigationAsync(FormResponseDefinitionRow definition, Guid recordId,
        ListFormRecordsRequest request, CancellationToken cancellationToken) =>
        ProjectNavigation(recordId, await NavigationIds(definition, request).ToArrayAsync(cancellationToken));

    private IQueryable<Guid> NavigationIds(FormResponseDefinitionRow definition, ListFormRecordsRequest request) =>
        Ordered(Filter(definition, request), definition, request).Select(record => record.Id);

    private static FormRecordNavigationRow? ProjectNavigation(Guid recordId, Guid[] orderedIds)
    {
        var index = Array.IndexOf(orderedIds, recordId);
        return index < 0 ? null : new(recordId, index == 0 ? null : orderedIds[index - 1],
            index == orderedIds.Length - 1 ? null : orderedIds[index + 1], index + 1, orderedIds.Length);
    }
}
