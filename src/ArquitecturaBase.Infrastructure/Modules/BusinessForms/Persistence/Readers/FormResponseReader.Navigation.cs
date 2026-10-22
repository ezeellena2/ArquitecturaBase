using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormResponseReader
{
    public async Task<FormRecordNavigationRow?> FindRecordNavigationAsync(FormResponseDefinitionRow definition, Guid recordId,
        ListFormRecordsRequest request, CancellationToken cancellationToken)
    {
        var row = await NavigationQuery(NavigationIds(definition, request), recordId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new(row.RecordId, row.PreviousId, row.NextId, row.Position, row.TotalCount);
    }

    private IQueryable<Guid> NavigationIds(FormResponseDefinitionRow definition, ListFormRecordsRequest request) =>
        Ordered(Filter(definition, request), definition, request).Select(record => record.Id);

    private IQueryable<NavigationRow> NavigationQuery(IQueryable<Guid> orderedIds, Guid recordId)
    {
        using var command = orderedIds.CreateDbCommand();
        var parameters = command.Parameters.Cast<NpgsqlParameter>().Select(parameter => (object)parameter.Clone())
            .Append(new NpgsqlParameter("navigation_record_id", recordId)).ToArray();
        var sql = FormRecordNavigationSql.Create(command.CommandText)
            .Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
        return dbContext.Database.SqlQueryRaw<NavigationRow>(sql, parameters);
    }

    private sealed class NavigationRow
    {
        public Guid RecordId { get; init; }
        public Guid? PreviousId { get; init; }
        public Guid? NextId { get; init; }
        public int Position { get; init; }
        public int TotalCount { get; init; }
    }
}
