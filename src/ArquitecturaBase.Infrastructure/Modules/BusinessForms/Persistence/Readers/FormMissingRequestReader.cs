using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormMissingRequestReader(ApplicationDbContext dbContext) : IFormMissingRequestReader
{
    private sealed class TopicAggregate
    {
        public Guid Id { get; init; }
        public long Revision { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string OriginalCulture { get; init; } = "";
        public int RequestCount { get; init; }
        public int PersonCount { get; init; }
        public DateTime LastRequestedAtUtc { get; init; }
        public BusinessFormMissingTopicState State { get; init; }
        public Guid? DiscardedByUserId { get; init; }
        public string? DiscardedByName { get; init; }
        public DateTime? DiscardedAtUtc { get; init; }
    }
    public async Task<IReadOnlyList<FormMissingTopicMetadata>> ListAssistantTopicsAsync(CancellationToken ct) =>
        await dbContext.Set<BusinessFormMissingTopic>().AsNoTracking().OrderBy(topic => topic.Id).Take(101)
            .Select(topic => new FormMissingTopicMetadata(topic.Id,topic.Name,topic.Description,topic.OriginalCulture)).ToArrayAsync(ct);

    public async Task<MissingFormTopicsPageResponse> FindPageAsync(ListMissingFormTopicsRequest request,CancellationToken ct)
    {
        var query = Aggregates();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            query = query.Where(topic => EF.Functions.ILike(topic.Name,pattern,LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(topic.Description,pattern,LikePatterns.EscapeCharacter));
        }
        var groups = await query.GroupBy(topic => topic.State).Select(group => new { State = group.Key,Count = group.Count() }).ToArrayAsync(ct);
        var counts = new MissingFormTopicStateCounts(groups.SingleOrDefault(group => group.State == BusinessFormMissingTopicState.Pending)?.Count ?? 0,
            groups.SingleOrDefault(group => group.State == BusinessFormMissingTopicState.Discarded)?.Count ?? 0);
        if (request.State is { } state) { var parsed = Enum.Parse<BusinessFormMissingTopicState>(state); query = query.Where(topic => topic.State == parsed); }
        var totalRequests = await query.SumAsync(topic => (int?)topic.RequestCount,ct) ?? 0;
        var sortable = new Dictionary<string,Expression<Func<TopicAggregate,object?>>>(StringComparer.OrdinalIgnoreCase)
            { ["lastRequestedAtUtc"] = topic => topic.LastRequestedAtUtc };
        var page = await Project(query.ApplySort(SortDescriptor.Parse(request.Sort),sortable,new("lastRequestedAtUtc",true),topic => topic.Id))
            .ToPagedResultAsync(request,ct);
        return new(page.Items,page.Page,page.PageSize,page.TotalCount,totalRequests,counts);
    }
    public Task<MissingFormTopicResponse?> FindAsync(Guid topicId,CancellationToken ct) => Project(Aggregates().Where(topic => topic.Id == topicId)).SingleOrDefaultAsync(ct);

    private IQueryable<TopicAggregate> Aggregates()
    {
        var requests = dbContext.Set<BusinessFormMissingRequest>().AsNoTracking().GroupBy(request => request.TopicId)
            .Select(group => new { TopicId = group.Key,RequestCount = group.Count(),PersonCount = group.Select(request => request.SubjectUserId).Distinct().Count(),
                LastRequestedAtUtc = group.Max(request => request.RequestedAtUtc) });
        return from topic in dbContext.Set<BusinessFormMissingTopic>().AsNoTracking()
               join aggregate in requests on topic.Id equals aggregate.TopicId
               select new TopicAggregate { Id = topic.Id,Revision = topic.Revision,Name = topic.Name,Description = topic.Description,OriginalCulture = topic.OriginalCulture,
                   RequestCount = aggregate.RequestCount,PersonCount = aggregate.PersonCount,LastRequestedAtUtc = aggregate.LastRequestedAtUtc,State = topic.State,
                   DiscardedByUserId = topic.DiscardedByUserId,DiscardedAtUtc = topic.DiscardedAtUtc,
                   DiscardedByName = dbContext.Users.Where(user => user.Id == topic.DiscardedByUserId).Select(user => user.DisplayName).FirstOrDefault() };
    }
    private static IQueryable<MissingFormTopicResponse> Project(IQueryable<TopicAggregate> query) => query.Select(topic => new MissingFormTopicResponse(
        topic.Id,topic.Revision,topic.Name,topic.Description,topic.OriginalCulture,topic.RequestCount,topic.PersonCount,topic.LastRequestedAtUtc,topic.State.ToString(),
        topic.DiscardedByUserId.HasValue ? new FormAuthorResponse(topic.DiscardedByUserId.Value,topic.DiscardedByName) : null,topic.DiscardedAtUtc));
}
