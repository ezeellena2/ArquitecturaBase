using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormMissingRequestFixture : IFormMissingRequestRepository, IFormMissingRequestReader
{
    public FormAssistantFixture Assistant { get; } = new();
    public FakeUnitOfWork Uow { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026,10,17,18,0,0,TimeSpan.Zero));
    public List<BusinessFormMissingTopic> Topics { get; } = [];
    public List<BusinessFormMissingRequest> Requests { get; } = [];
    public Func<Task>? OnLock { get; set; }
    public Action? OnRead { get; set; }
    public int Locks { get; private set; }
    public FormMissingRequestService Service { get; }
    public Guid Manager { get; }
    public FormMissingRequestFixture()
    {
        Assistant.Permissions.Permissions[Assistant.UserId] = [];
        Manager = Assistant.Users.AddUser("manager@example.invalid").Id;
        Assistant.Permissions.Permissions[Manager] = [BusinessFormsPermissions.Manage];
        Current.UserId = Manager;
        Service = new(this,this,Assistant.Users,Assistant.Permissions,Current,Uow,Time,
            RequestValidators.For(new ListMissingFormTopicsRequestValidator()));
    }
    public StructuredMissingFormRequestCaptureRequest Request(Guid? source = null, Guid? subject = null, BusinessFormMissingTopic? existing = null)
    {
        var user = subject ?? Assistant.UserId; var turn = Guid.CreateVersion7();
        var metadata = Topics.Select(topic => new FormMissingTopicMetadata(topic.Id,topic.Name,topic.Description,topic.OriginalCulture)).ToArray();
        var payload = new MissingFormRequestProposalPayload("proposed",1,existing?.Id,existing is null ? "Registrar mantenimiento" : null,
            existing is null ? "Piden registrar tareas de mantenimiento." : null);
        var observed = new AssistantObservedRead(turn,1,new(BusinessFormAssistantCapabilitySource.CandidatesId,1),
            Assistant.ReadPayload(user) with { Forms = [], Topics = metadata });
        var context = Assistant.Context with { ActorUserId = user, SubjectUserId = user,
            AllowedCapabilities = [new(BusinessFormMissingRequestAssistantCapabilitySource.ProposeId,1)] };
        return new(source ?? Guid.CreateVersion7(),Time.GetUtcNow().UtcDateTime.AddMinutes(-1),context,
            new(turn,Guid.CreateVersion7(),new(BusinessFormMissingRequestAssistantCapabilitySource.ProposeId,1),payload) { ObservedReads = [observed] },
            new("test-profile",1,"test-instructions",1,Time.GetUtcNow().UtcDateTime,"whatsapp-assistant"));
    }
    public async Task LockAsync(CancellationToken ct)
    {
        TransactionGuard.Require(() => Uow.InTransaction); Locks++;
        if (OnLock is { } action) { OnLock = null; await action(); }
    }
    public Task<BusinessFormMissingRequest?> GetRequestAsync(Guid id,CancellationToken ct) => Task.FromResult(Requests.SingleOrDefault(request => request.SourceReceiptId == id));
    public Task<BusinessFormMissingTopic?> GetAsync(Guid id,CancellationToken ct) => Task.FromResult(Topics.SingleOrDefault(topic => topic.Id == id));
    public Task<BusinessFormMissingTopic?> GetByNameAsync(string name,CancellationToken ct) => Task.FromResult(Topics.SingleOrDefault(topic => topic.NormalizedName == name));
    public void Add(BusinessFormMissingTopic topic) { TransactionGuard.Require(() => Uow.InTransaction); Topics.Add(topic); }
    public void Add(BusinessFormMissingRequest request) { TransactionGuard.Require(() => Uow.InTransaction); Requests.Add(request); }
    public Task<IReadOnlyList<FormMissingTopicMetadata>> ListAssistantTopicsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<FormMissingTopicMetadata>>(
        Topics.Select(topic => new FormMissingTopicMetadata(topic.Id,topic.Name,topic.Description,topic.OriginalCulture)).ToArray());
    public Task<MissingFormTopicResponse?> FindAsync(Guid id,CancellationToken ct)
    { var topic = Topics.SingleOrDefault(topic => topic.Id == id); var row = topic is null ? null : Map(topic); OnRead?.Invoke(); OnRead = null; return Task.FromResult(row); }
    public Task<MissingFormTopicsPageResponse> FindPageAsync(ListMissingFormTopicsRequest request,CancellationToken ct)
    {
        var rows = Topics.Select(Map).Where(row => request.Search is null || row.Name.Contains(request.Search,StringComparison.OrdinalIgnoreCase)
            || row.Description.Contains(request.Search,StringComparison.OrdinalIgnoreCase)).ToArray();
        var filtered = rows.Where(row => request.State is null || row.State == request.State).OrderByDescending(row => row.LastRequestedAtUtc).ThenBy(row => row.Id).ToArray();
        var page = new MissingFormTopicsPageResponse(filtered.Skip((request.Page-1)*request.PageSize).Take(request.PageSize).ToArray(),request.Page,request.PageSize,
            filtered.Length,filtered.Sum(row => row.RequestCount),new(rows.Count(row => row.State == "Pending"),rows.Count(row => row.State == "Discarded")));
        OnRead?.Invoke(); OnRead = null; return Task.FromResult(page);
    }
    private MissingFormTopicResponse Map(BusinessFormMissingTopic topic)
    {
        var requests = Requests.Where(request => request.TopicId == topic.Id).ToArray();
        return new(topic.Id,topic.Revision,topic.Name,topic.Description,topic.OriginalCulture,requests.Length,
            requests.Select(request => request.SubjectUserId).Distinct().Count(),requests.Max(request => request.RequestedAtUtc),topic.State.ToString(),
            topic.DiscardedByUserId is { } actor ? new(actor,Assistant.Users.Users.Single(user => user.Id == actor).DisplayName) : null,topic.DiscardedAtUtc);
    }
}
