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
        bool canReadRecords, bool canDecide, CancellationToken cancellationToken)
    {
        var query = Query(actorUserId, request, culture, canReadRecords, canDecide);
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
        bool canReadRecords, bool canDecide)
    {
        var nameColumn = culture == "en" ? "DisplayNameEn" : "DisplayNameEs";
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
                        ResponseCount = canReadRecords ? definition.LastRecordNumber : null,
                        LastRecordId = canReadRecords ? db.Set<BusinessFormRecord>().Where(record => record.DefinitionId == definition.Id
                            && record.Number == definition.LastRecordNumber).Select(record => (Guid?)record.Id).SingleOrDefault() : null,
                        LastNumber = canReadRecords ? db.Set<BusinessFormRecord>().Where(record => record.DefinitionId == definition.Id
                            && record.Number == definition.LastRecordNumber).Select(record => (long?)record.Number).SingleOrDefault() : null,
                        LastSubmittedAtUtc = canReadRecords ? db.Set<BusinessFormRecord>().Where(record => record.DefinitionId == definition.Id
                            && record.Number == definition.LastRecordNumber).Select(record => (DateTime?)record.SubmittedAtUtc).SingleOrDefault() : null,
                        LastState = canReadRecords ? db.Set<BusinessFormRecord>().Where(record => record.DefinitionId == definition.Id
                            && record.Number == definition.LastRecordNumber).Select(record => record.State.ToString()).SingleOrDefault() : null,
                    };
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            query = query.Where(row => EF.Functions.ILike(row.Name, pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(row.Prefix, pattern, LikePatterns.EscapeCharacter));
        }
        if (!canReadRecords || request.PendingOnly) query = query.Where(row => row.PendingApprovalCount > 0);
        return query.OrderByDescending(row => row.PendingApprovalCount > 0)
            .ThenByDescending(row => row.LastSubmittedAtUtc != null).ThenByDescending(row => row.LastSubmittedAtUtc)
            .ThenBy(row => row.Name).ThenBy(row => row.Id);
    }

    private IQueryable<BusinessFormDefinition> Definitions() => db.Set<BusinessFormDefinition>().AsNoTracking()
        .Where(definition => definition.ActiveVersionId != null
            && db.Set<BusinessFormPublicationOperation>().Any(operation => operation.DefinitionId == definition.Id
                && operation.VersionId == definition.ActiveVersionId && operation.State == FormPublicationState.Published));

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