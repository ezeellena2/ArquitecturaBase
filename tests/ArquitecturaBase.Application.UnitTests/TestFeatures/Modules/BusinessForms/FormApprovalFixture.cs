using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormApprovalFixture : IFormApprovalRepository, IFormApprovalReader
{
    public FormActionFixture Actions { get; } = new();
    public FakeCurrentUser User { get; } = new() { UserId = Guid.NewGuid() };
    public FakePermissionService Permissions { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public BusinessFormRecord Record { get; }
    public BusinessFormTask Task { get; }
    public FormActionRecordSnapshot Snapshot { get; }
    public FormApprovalService Service { get; }
    public bool IsInRole { get; set; } = true;
    public Action? BeforeLock { get; set; }
    public Action? BeforeReadReturn { get; set; }
    public FormApprovalFixture()
    {
        Permissions.Permissions[User.UserId!.Value] = [BusinessFormsPermissions.ApprovalsDecide];
        var captured = Actions.Capture([new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action()]), new(Guid.NewGuid(), "Rejected", [], [FormActionFixture.Action()])]);
        Record = captured.Record; Snapshot = captured.Snapshot; Record.MarkPendingApproval();
        Task = BusinessFormTask.Open(Record.DefinitionId, Record.Id, Record.RuleSetId!.Value, Guid.NewGuid(), Guid.NewGuid(), Record.ActorUserId, Actions.Clock.GetUtcNow().UtcDateTime);
        Service = new(this, this, new(User, Permissions, this), UnitOfWork, RequestValidators.For(new ListFormApprovalsRequestValidator(), new DecideFormApprovalRequestValidator()),
            Actions.Clock, NullLogger<FormApprovalService>.Instance, Actions.Recorder);
    }
    public System.Threading.Tasks.Task LockAsync(CancellationToken ct) { BeforeLock?.Invoke();
    return System.Threading.Tasks.Task.CompletedTask; }
    public Task<BusinessFormTask?> GetAsync(Guid id, CancellationToken ct) => System.Threading.Tasks.Task.FromResult<BusinessFormTask?>(id == Task.Id ? Task : null);
    public Task<BusinessFormRecord?> GetRecordAsync(Guid id, CancellationToken ct) => System.Threading.Tasks.Task.FromResult<BusinessFormRecord?>(id == Record.Id ? Record : null);
    public Task<FormApprovalActorRow?> FindActorAsync(Guid actor, Guid? role, CancellationToken ct) => System.Threading.Tasks.Task.FromResult<FormApprovalActorRow?>(new(actor, "Approver", true, role is null || IsInRole));
    public Task<PagedResult<FormApprovalTaskResponse>> ListAsync(Guid actor, ListFormApprovalsRequest request, string culture, CancellationToken ct)
    { BeforeReadReturn?.Invoke();
    return System.Threading.Tasks.Task.FromResult(new PagedResult<FormApprovalTaskResponse>([Response(actor)], 1, 20, 1)); }
    public Task<int> CountAsync(Guid actor, FormApprovalFilterRequest request, CancellationToken ct) { BeforeReadReturn?.Invoke();
    return System.Threading.Tasks.Task.FromResult(1); }
    public Task<IReadOnlyList<FormApprovalFormResponse>> ListFormsAsync(Guid actor, bool onlyMine, string culture, CancellationToken ct) { BeforeReadReturn?.Invoke();
    return System.Threading.Tasks.Task.FromResult<IReadOnlyList<FormApprovalFormResponse>>([new(Record.DefinitionId, "Visits")]); }
    public Task<FormApprovalTaskResponse?> FindAsync(Guid id, Guid actor, string culture, CancellationToken ct) { BeforeReadReturn?.Invoke();
    return System.Threading.Tasks.Task.FromResult<FormApprovalTaskResponse?>(Response(actor)); }
    public Task<FormActionRecordSnapshot?> FindActionSnapshotAsync(Guid recordId, CancellationToken ct) => System.Threading.Tasks.Task.FromResult<FormActionRecordSnapshot?>(Snapshot);
    private FormApprovalTaskResponse Response(Guid actor) => new(Task.Id, Record.DefinitionId, Record.Id, "VIS-000001", "Visits", new(Record.ActorUserId, "Author"), new(Task.RoleId, "Supervisor"), Task.RequestedAtUtc,
        Task.Revision, Task.State.ToString(), Task.State == FormTaskState.Open && actor != Task.AuthorUserId, actor == Task.AuthorUserId ? "OwnResponse" : null, Task.DecidedAtUtc,
        Task.DecidedByUserId is { } id ? new(id, "Approver") : null, Task.Reason);
}
