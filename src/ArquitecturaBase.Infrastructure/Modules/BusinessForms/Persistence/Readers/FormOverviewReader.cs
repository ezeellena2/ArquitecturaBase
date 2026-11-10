using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormOverviewReader(ApplicationDbContext db) : IFormOverviewReader
{
    public async Task<FormOverviewResponse> FindPageAsync(Guid actorUserId, ListFormOverviewRequest request, string culture,
        bool canReadRecords, bool canDecide, bool canCreate, CancellationToken cancellationToken)
    {
        var query = Query(actorUserId, request, culture, canReadRecords, canDecide, canCreate);
        var counts = await query.GroupBy(_ => 1).Select(group => new FormOverviewCounts(
            group.Count(row => row.DeactivatedAtUtc == null),
            group.Count(row => row.DeactivatedAtUtc != null),
            canDecide ? group.Count(row => row.DeactivatedAtUtc != null && row.PendingApprovalCount > 0) : null))
            .SingleOrDefaultAsync(cancellationToken) ?? new(0, 0, canDecide ? 0 : null);
        if (!(canReadRecords && canDecide && request.PendingOnly))
            query = request.State == "Deactivated"
                ? query.Where(row => row.DeactivatedAtUtc != null)
                : query.Where(row => row.DeactivatedAtUtc == null);
        var page = await query.ToPagedResultAsync(request, cancellationToken);
        return new(new(page.Items.Select(row => new FormOverviewFormResponse(row.Id, row.Name, row.Prefix,
            row.DeactivatedAtUtc, row.PendingApprovalCount, row.ResponseCount,
            row.LastRecordId is { } id ? new(id, row.Prefix + "-" + row.LastNumber!.Value.ToString("D6", CultureInfo.InvariantCulture),
                row.LastSubmittedAtUtc!.Value, row.LastState!) : null)).ToArray(), page.Page, page.PageSize, page.TotalCount), counts);
    }

    private IQueryable<OverviewRow> Query(Guid actorUserId, ListFormOverviewRequest request, string culture,
        bool canReadRecords, bool canDecide, bool canCreate)
    {
        var nameColumn = culture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        // Whoever only loads counts their own responses and sees their last one (CW6-C).
        var own = !canReadRecords && canCreate;
        // Only current responses that were not voided count. A correction keeps its number, so the count is no longer
        // LastRecordNumber; the last one is the current version with the highest number.
        var counted = db.Set<BusinessFormRecord>().Where(record => record.ReplacedByRecordId == null && record.State != FormRecordState.Voided)
            .OrderByDescending(record => record.Number);
        var mine = counted.Where(record => record.ActorUserId == actorUserId);
        var query = from definition in Definitions()
                    join version in db.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.PublishedCultures != null)
                        on definition.ActiveVersionId equals version.Id
                    select new OverviewRow
                    {
                        Id = definition.Id,
                        Name = version.PublishedCultures!.Contains(culture) ? EF.Property<string>(version, nameColumn)
                            : version.Culture == "en" ? EF.Property<string>(version, "DisplayNameEn") : EF.Property<string>(version, "DisplayNameEs"),
                        Prefix = definition.Prefix,
                        DeactivatedAtUtc = definition.DeactivatedAtUtc,
                        PendingApprovalCount = canDecide ? db.Set<BusinessFormTask>().Count(task =>
                            task.DefinitionId == definition.Id && task.State == FormTaskState.Open && task.AuthorUserId != actorUserId
                            && db.Users.Any(user => user.Id == actorUserId && user.IsActive)
                            && db.UserRoles.Any(link => link.UserId == actorUserId && link.RoleId == task.RoleId)) : null,
                        ResponseCount = canReadRecords ? counted.LongCount(record => record.DefinitionId == definition.Id)
                            : own ? mine.LongCount(record => record.DefinitionId == definition.Id) : null,
                        LastRecordId = canReadRecords ? counted.Where(record => record.DefinitionId == definition.Id).Select(record => (Guid?)record.Id).FirstOrDefault()
                            : own ? mine.Where(record => record.DefinitionId == definition.Id).Select(record => (Guid?)record.Id).FirstOrDefault() : null,
                        LastNumber = canReadRecords ? counted.Where(record => record.DefinitionId == definition.Id).Select(record => (long?)record.Number).FirstOrDefault()
                            : own ? mine.Where(record => record.DefinitionId == definition.Id).Select(record => (long?)record.Number).FirstOrDefault() : null,
                        LastSubmittedAtUtc = canReadRecords ? counted.Where(record => record.DefinitionId == definition.Id).Select(record => (DateTime?)record.SubmittedAtUtc).FirstOrDefault()
                            : own ? mine.Where(record => record.DefinitionId == definition.Id).Select(record => (DateTime?)record.SubmittedAtUtc).FirstOrDefault() : null,
                        LastState = canReadRecords ? counted.Where(record => record.DefinitionId == definition.Id).Select(record => record.State.ToString()).FirstOrDefault()
                            : own ? mine.Where(record => record.DefinitionId == definition.Id).Select(record => record.State.ToString()).FirstOrDefault() : null,
                    };
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            query = query.Where(row => EF.Functions.ILike(row.Name, pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(row.Prefix, pattern, LikePatterns.EscapeCharacter));
        }
        if (!canReadRecords && !canCreate || request.PendingOnly) query = query.Where(row => row.PendingApprovalCount > 0);
        return query.OrderByDescending(row => row.PendingApprovalCount > 0)
            .ThenByDescending(row => row.LastSubmittedAtUtc != null).ThenByDescending(row => row.LastSubmittedAtUtc)
            .ThenBy(row => row.Name).ThenBy(row => row.Id);
    }

    private IQueryable<BusinessFormDefinition> Definitions() => FormOverviewDefinitionQuery.Published(db);

    private sealed class OverviewRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public string Prefix { get; init; } = "";
        public DateTime? DeactivatedAtUtc { get; init; }
        public int? PendingApprovalCount { get; init; }
        public long? ResponseCount { get; init; }
        public Guid? LastRecordId { get; init; }
        public long? LastNumber { get; init; }
        public DateTime? LastSubmittedAtUtc { get; init; }
        public string? LastState { get; init; }
    }
}