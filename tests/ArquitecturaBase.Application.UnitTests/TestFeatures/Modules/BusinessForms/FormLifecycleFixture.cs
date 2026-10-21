using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormLifecycleFixture : IFormDraftRepository, IFormPublicationRepository, IFormLifecycleReader
{
    public FakeCurrentUser User { get; } = new() { UserId = Guid.NewGuid() };
    public FakePermissionService Permissions { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    public BusinessFormDefinition Definition { get; } = BusinessFormDefinition.Create();
    public bool PublicationOpen { get; set; }
    public int PendingApprovalCount { get; set; } = 2;
    public Action? BeforeLock { get; set; }
    public Action? BeforeBatchRead { get; set; }
    public Action? BeforeBatchReturn { get; set; }
    public int EntityReads { get; private set; }
    public int BatchReads { get; private set; }
    public IReadOnlyList<FormIncomingSuggestionResponse> Incoming { get; set; } = [];
    public IFormLifecycleService Service { get; }

    public FormLifecycleFixture(bool published = true)
    {
        Permissions.Permissions[User.UserId!.Value] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish, BusinessFormsPermissions.RecordsRead];
        Definition.SetDraft(1, "Form", "{\"es\":\"Form\"}", "FORM", "es", "{\"es\":\"Enviar\"}", "{\"format\":1,\"fields\":[]}", 0, true);
        if (published) { Definition.LockPrefix(); Definition.ActivateVersion(null, Guid.NewGuid()); }
        Service = new FormLifecycleService(this, this, this, new(User, Permissions, RequestValidators.For(new DeactivateFormRequestValidator())),
            UnitOfWork, Clock, NullLogger<FormLifecycleService>.Instance);
    }
    public Task LockDefinitionsAsync(CancellationToken ct)
    { Assert.True(UnitOfWork.InTransaction); BeforeLock?.Invoke(); return Task.CompletedTask; }
    public Task<BusinessFormDefinition?> GetAsync(Guid id, CancellationToken ct)
    { Assert.True(UnitOfWork.InTransaction); EntityReads++; return Task.FromResult(id == Definition.Id ? Definition : null); }
    public Task<bool> ExistsOpenOperationAsync(Guid id, CancellationToken ct) => Task.FromResult(PublicationOpen);
    public Task<IReadOnlyList<FormLifecycleStoredRow>> ListFactsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        BatchReads++; BeforeBatchRead?.Invoke();
        IReadOnlyList<FormLifecycleStoredRow> rows = ids.Contains(Definition.Id) ? [new(Definition.Id, Definition.Revision, Definition.Prefix,
            Definition.ActiveVersionId, Definition.ActiveVersionId.HasValue ? 2 : 1, Definition.LastRecordNumber, PendingApprovalCount,
            Definition.DeactivatedAtUtc, Definition.DeactivatedBy is {} actor ? new(actor, "Publisher") : null, PublicationOpen)] : [];
        BeforeBatchReturn?.Invoke(); return Task.FromResult(rows);
    }
    public Task<IReadOnlyList<FormIncomingSuggestionResponse>> ListIncomingSuggestionsAsync(Guid id, CancellationToken ct) => Task.FromResult(Incoming);
    public Task LockRequestAsync(Guid actor, Guid request, CancellationToken ct) => throw new NotSupportedException();
    public Task<BusinessFormDraftChange?> GetChangeAsync(Guid actor, Guid request, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsPrefixAsync(string prefix, Guid except, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormRetiredIdentities> FindRetiredIdentitiesAsync(Guid id, IReadOnlyCollection<Guid> fields, IReadOnlyCollection<Guid> choices, CancellationToken ct) => throw new NotSupportedException();
    public void AddRetiredIdentities(Guid id, FormRetiredIdentities identities) => throw new NotSupportedException();
    public void Add(BusinessFormDefinition definition) => throw new NotSupportedException();
    public void AddChange(BusinessFormDraftChange change) => throw new NotSupportedException();
    public Task UpdateExpiredResultsAsync(DateTime now, CancellationToken ct) => throw new NotSupportedException();
    public Task<BusinessFormPublicationOperation?> GetOperationAsync(Guid actor, Guid request, CancellationToken ct) => throw new NotSupportedException();
    public Task<BusinessFormPublicationOperation?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> CountVersionsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<BusinessFormField>> ListFieldsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public void AddField(BusinessFormField field) => throw new NotSupportedException();
    public void AddVersion(BusinessFormVersion version) => throw new NotSupportedException();
    public void AddOperation(BusinessFormPublicationOperation operation) => throw new NotSupportedException();
}
