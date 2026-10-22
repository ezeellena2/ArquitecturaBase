using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed partial class FormApprovalReader
{
    public async Task<FormApprovalContinuationResponse?> FindContinuationAsync(Guid id, Guid actor, FormApprovalContinuationRequest request,
        string culture, CancellationToken cancellationToken)
    {
        var snapshot = await ContinuationQuery(id, actor, request).ToArrayAsync(cancellationToken);
        if (snapshot.Length == 0) return null;
        var header = snapshot[0];
        var rows = snapshot.Where(row => row.Id != null).ToArray();
        if (rows.Length == 0) return new(header.AnchorTaskId, request.DefinitionId, request.Direction!, [], header.TotalCount, header.HasMore);
        var versions = await records.ListVersionsAsync(rows.Select(row => row.VersionId!.Value).Distinct().ToArray(), cancellationToken);
        var items = rows.Select(row => new FormApprovalContinuationItemResponse(Map(ContinuationTask(row), actor, culture,
            versions[row.VersionId!.Value]), row.Position!.Value)).ToArray();
        return new(header.AnchorTaskId, request.DefinitionId, request.Direction!, items, header.TotalCount, header.HasMore);
    }

    private IQueryable<ContinuationSqlRow> ContinuationQuery(Guid id, Guid actor, FormApprovalContinuationRequest request)
    {
        var (comparison, order) = request.Direction switch
        {
            "Next" => (">", "ASC"), "Previous" => ("<", "DESC"),
            _ => throw new InvalidOperationException("The approval continuation direction is invalid.")
        };
        using var scope = Authorized(actor).Where(task => task.DefinitionId == request.DefinitionId && task.AuthorUserId != actor
                && dbContext.Set<BusinessFormDefinition>().Any(definition => definition.Id == task.DefinitionId))
            .Select(task => new { task.Id, task.RequestedAtUtc, task.State }).CreateDbCommand();
        var batchIds = dbContext.Database.SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM continuation_batch");
        using var metadata = Project(dbContext.Set<BusinessFormTask>().AsNoTracking().Where(task => batchIds.Contains(task.Id))).CreateDbCommand();
        var parameters = scope.Parameters.Cast<NpgsqlParameter>().Concat(metadata.Parameters.Cast<NpgsqlParameter>())
            .Select(parameter => (object)parameter.Clone()).Concat([
                new NpgsqlParameter("continuation_anchor_id", NpgsqlDbType.Uuid) { Value = id },
                new NpgsqlParameter("continuation_page_size", NpgsqlDbType.Integer) { Value = request.PageSize },
                new NpgsqlParameter("continuation_take", NpgsqlDbType.Integer) { Value = request.PageSize + 1 }]).ToArray();
        var sql = $"""
            WITH continuation_scope AS (
                {scope.CommandText}
            ), continuation_anchor AS (
                SELECT "Id", "RequestedAtUtc" FROM continuation_scope WHERE "Id" = @continuation_anchor_id
            ), continuation_open AS (
                SELECT "Id", "RequestedAtUtc",
                    (ROW_NUMBER() OVER (ORDER BY "RequestedAtUtc", "Id"))::integer AS "Position",
                    (COUNT(*) OVER ())::integer AS "TotalCount"
                FROM continuation_scope WHERE "State" = 'Open'
            ), continuation_candidates AS (
                SELECT pending.* FROM continuation_open AS pending CROSS JOIN continuation_anchor AS anchor
                WHERE (pending."RequestedAtUtc", pending."Id") {comparison} (anchor."RequestedAtUtc", anchor."Id")
                ORDER BY pending."RequestedAtUtc" {order}, pending."Id" {order}
                LIMIT @continuation_take
            ), continuation_batch AS (
                SELECT * FROM continuation_candidates
                ORDER BY "RequestedAtUtc" {order}, "Id" {order}
                LIMIT @continuation_page_size
            ), continuation_metadata AS (
                {metadata.CommandText}
            )
            SELECT anchor."Id" AS "AnchorTaskId",
                COALESCE((SELECT MAX("TotalCount") FROM continuation_open), 0)::integer AS "TotalCount",
                ((SELECT COUNT(*) FROM continuation_candidates) > @continuation_page_size) AS "HasMore",
                batch."Position", detail.*
            FROM continuation_anchor AS anchor
            LEFT JOIN continuation_batch AS batch ON TRUE
            LEFT JOIN continuation_metadata AS detail ON detail."Id" = batch."Id"
            ORDER BY batch."Position"
            """;
        sql = sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
        return dbContext.Database.SqlQueryRaw<ContinuationSqlRow>(sql, parameters);
    }

    private static ApprovalRow ContinuationTask(ContinuationSqlRow row)
    {
        if (!Enum.TryParse<FormTaskState>(row.State, out var state)) throw IncompleteContinuation();
        return new()
        {
            Id = row.Id ?? throw IncompleteContinuation(), DefinitionId = row.DefinitionId ?? throw IncompleteContinuation(),
            RecordId = row.RecordId ?? throw IncompleteContinuation(), Number = row.Number ?? throw IncompleteContinuation(),
            Prefix = row.Prefix ?? throw IncompleteContinuation(), VersionId = row.VersionId ?? throw IncompleteContinuation(),
            AuthorUserId = row.AuthorUserId ?? throw IncompleteContinuation(), AuthorName = row.AuthorName,
            RoleId = row.RoleId ?? throw IncompleteContinuation(), RoleName = row.RoleName,
            RequestedAtUtc = row.RequestedAtUtc ?? throw IncompleteContinuation(), Revision = row.Revision ?? throw IncompleteContinuation(),
            State = state, DecidedAtUtc = row.DecidedAtUtc, DecidedByUserId = row.DecidedByUserId,
            DecidedByName = row.DecidedByName, Reason = row.Reason
        };
    }
    private static InvalidOperationException IncompleteContinuation() => new("The approval continuation metadata is incomplete.");

    private sealed class ContinuationSqlRow
    {
        public Guid AnchorTaskId { get; init; }
        public int TotalCount { get; init; }
        public bool HasMore { get; init; }
        public int? Position { get; init; }
        public Guid? Id { get; init; }
        public Guid? DefinitionId { get; init; }
        public Guid? RecordId { get; init; }
        public long? Number { get; init; }
        public string? Prefix { get; init; }
        public Guid? VersionId { get; init; }
        public Guid? AuthorUserId { get; init; }
        public string? AuthorName { get; init; }
        public Guid? RoleId { get; init; }
        public string? RoleName { get; init; }
        public DateTime? RequestedAtUtc { get; init; }
        public long? Revision { get; init; }
        public string? State { get; init; }
        public DateTime? DecidedAtUtc { get; init; }
        public Guid? DecidedByUserId { get; init; }
        public string? DecidedByName { get; init; }
        public string? Reason { get; init; }
    }
}