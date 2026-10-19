using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormApprovalReader(ApplicationDbContext dbContext, IFormResponseReader responses, IFormRecordReader records) : IFormApprovalReader
{
    public Task<FormApprovalActorRow?> FindActorAsync(Guid userId, Guid? roleId, CancellationToken cancellationToken) => dbContext.Users.AsNoTracking().Where(user => user.Id == userId)
        .Select(user => new FormApprovalActorRow(user.Id, user.DisplayName, user.IsActive, roleId == null || dbContext.UserRoles.Any(link => link.UserId == userId && link.RoleId == roleId))).SingleOrDefaultAsync(cancellationToken);
    public async Task<PagedResult<FormApprovalTaskResponse>> ListAsync(Guid actor, ListFormApprovalsRequest request, string culture, CancellationToken cancellationToken)
    {
        var sortable = new Dictionary<string, Expression<Func<ApprovalRow, object?>>>(StringComparer.OrdinalIgnoreCase) { ["requestedAtUtc"] = task => task.RequestedAtUtc };
        var tasks = Project(Open(actor, new(request.DefinitionId, request.OnlyMine)))
            .ApplySort(SortDescriptor.Parse(null), sortable, new("requestedAtUtc", false), task => task.Id);
        var page = await tasks.ToPagedResultAsync(request, cancellationToken);
        var versions = await records.ListVersionsAsync(page.Items.Select(row => row.VersionId).Distinct().ToArray(), cancellationToken);
        return new(page.Items.Select(row => Map(row, actor, culture, versions[row.VersionId])).ToArray(), page.Page, page.PageSize, page.TotalCount);
    }
    public Task<int> CountAsync(Guid actor, FormApprovalFilterRequest request, CancellationToken cancellationToken) => Open(actor, request).CountAsync(cancellationToken);
    public async Task<IReadOnlyList<FormApprovalFormResponse>> ListFormsAsync(Guid actor, bool onlyMine, string culture, CancellationToken cancellationToken)
    {
        var tasks = Open(actor, new(null, onlyMine));
        var query = from definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking()
            join version in dbContext.Set<BusinessFormVersion>().AsNoTracking() on definition.ActiveVersionId equals version.Id
            where tasks.Any(task => task.DefinitionId == definition.Id)
            select new { definition.Id, VersionId = version.Id };
        var versions = await records.ListVersionsAsync(await query.Select(row => row.VersionId).ToArrayAsync(cancellationToken), cancellationToken);
        var names = FormVersionPresentationQuery.Names(dbContext, versions.Values, culture);
        return await (from row in query join name in names on row.VersionId equals name.VersionId
            orderby name.Name, row.Id select new FormApprovalFormResponse(row.Id, name.Name)).ToArrayAsync(cancellationToken);
    }
    public async Task<FormApprovalTaskResponse?> FindAsync(Guid id, Guid actor, string culture, CancellationToken cancellationToken)
    {
        var row = await Project(Authorized(actor).Where(task => task.Id == id)).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var versions = await records.ListVersionsAsync([row.VersionId], cancellationToken);
        return Map(row, actor, culture, versions[row.VersionId]);
    }
    public async Task<FormActionRecordSnapshot?> FindActionSnapshotAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var row = await responses.FindRecordAsync(recordId, cancellationToken);
        if (row is null) return null;
        return new(row, (row.Version ?? throw new InvalidOperationException("The sealed response culture is missing.")).OriginalCulture);
    }
    private IQueryable<BusinessFormTask> Authorized(Guid actor) => dbContext.Set<BusinessFormTask>().AsNoTracking()
        .Where(task => dbContext.Users.Any(user => user.Id == actor && user.IsActive) && dbContext.UserRoles.Any(link => link.UserId == actor && link.RoleId == task.RoleId));
    private IQueryable<BusinessFormTask> Open(Guid actor, FormApprovalFilterRequest filter)
    {
        var query = Authorized(actor).Where(task => task.State == FormTaskState.Open);
        if (filter.DefinitionId is { } id) query = query.Where(task => task.DefinitionId == id);
        if (filter.OnlyMine) query = query.Where(task => task.AuthorUserId != actor);
        return query;
    }
    private IQueryable<ApprovalRow> Project(IQueryable<BusinessFormTask> tasks) => from task in tasks
                                                                                   join record in dbContext.Set<BusinessFormRecord>().AsNoTracking() on task.RecordId equals record.Id
                                                                                   join definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking() on task.DefinitionId equals definition.Id
                                                                                   join version in dbContext.Set<BusinessFormVersion>().AsNoTracking() on record.VersionId equals version.Id
                                                                                   select new ApprovalRow
                                                                                   {
                                                                                       Id = task.Id,
                                                                                       DefinitionId = task.DefinitionId,
                                                                                       RecordId = task.RecordId,
                                                                                       Number = record.Number,
                                                                                       Prefix = definition.Prefix,
                                                                                       VersionId = version.Id,
                                                                                       AuthorUserId = task.AuthorUserId,
                                                                                       AuthorName = dbContext.Users.Where(user => user.Id == task.AuthorUserId).Select(user => user.DisplayName).FirstOrDefault(),
                                                                                       RoleId = task.RoleId,
                                                                                       RoleName = dbContext.Roles.Where(role => role.Id == task.RoleId).Select(role => role.Name).FirstOrDefault(),
                                                                                       RequestedAtUtc = task.RequestedAtUtc,
                                                                                       Revision = task.Revision,
                                                                                       State = task.State,
                                                                                       DecidedAtUtc = task.DecidedAtUtc,
                                                                                       DecidedByUserId = task.DecidedByUserId,
                                                                                       DecidedByName = dbContext.Users.Where(user => user.Id == task.DecidedByUserId).Select(user => user.DisplayName).FirstOrDefault(),
                                                                                       Reason = task.Reason
                                                                                   };
    private static FormApprovalTaskResponse Map(ApprovalRow row, Guid actor, string culture, FormResponseVersionRow version) => new(row.Id, row.DefinitionId, row.RecordId, row.Prefix + "-" + row.Number.ToString("D6", CultureInfo.InvariantCulture), Name(version, culture),
        new(row.AuthorUserId, row.AuthorName), new(row.RoleId, row.RoleName ?? ""), row.RequestedAtUtc, row.Revision, row.State.ToString(), row.State == FormTaskState.Open && row.AuthorUserId != actor,
        row.State != FormTaskState.Open ? "AlreadyDecided" : row.AuthorUserId == actor ? "OwnResponse" : null, row.DecidedAtUtc, row.DecidedByUserId is { } decider ? new(decider, row.DecidedByName) : null, row.Reason);
    private static string Name(FormResponseVersionRow version, string culture) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(version.NameJson)![version.DisplayCulture(culture)];
    private sealed class ApprovalRow
    {
        public Guid Id { get; init; }
        public Guid DefinitionId { get; init; }
        public Guid RecordId { get; init; }
        public long Number { get; init; }
        public string Prefix { get; init; } = "";
        public Guid VersionId { get; init; }
        public Guid AuthorUserId { get; init; }
        public string? AuthorName { get; init; }
        public Guid RoleId { get; init; }
        public string? RoleName { get; init; }
        public DateTime RequestedAtUtc { get; init; }
        public long Revision { get; init; }
        public FormTaskState State { get; init; }
        public DateTime? DecidedAtUtc { get; init; }
        public Guid? DecidedByUserId { get; init; }
        public string? DecidedByName { get; init; }
        public string? Reason { get; init; }
    }
}
