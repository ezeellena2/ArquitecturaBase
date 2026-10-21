using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppFlowDeprecationFixture : IWhatsAppFlowRepository, IWhatsAppFlowReader,
    IWhatsAppFlowProtector, IStructuredFormRetirementSource
{
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc));
    public WhatsAppFlowOperation Operation { get; }
    public WhatsAppFlowBinding Binding { get; }
    public List<string> Locks { get; } = [];
    public bool Unused { get; set; } = true;
    public bool HasOpenWork { get; set; }
    public bool UnreadableManifest { get; set; }
    public WhatsAppFlowStoredManifest Manifest { get; } = new(2, "request", "publication",
        new("222", "111", "v25.0", "configuration"), ["formtemplates.publish"]);
    public DateTime Now => Clock.GetUtcNow().UtcDateTime;
    public WhatsAppFlowDeprecationFixture()
    {
        Operation = WhatsAppFlowOperation.Accept(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Operation.SealManifest([1]); Operation.Release(Now, true, false);
        Binding = WhatsAppFlowBinding.Create(Operation.Id, Operation.VersionId, "es", "fixed", [1, 2, 3]);
        Binding.Begin(1, Now); Binding.MarkEffectStarted(1); Binding.RecordEffect(1, Now, "333", true, false, false);
        Binding.Begin(2, Now); Binding.MarkEffectStarted(2); Binding.RecordEffect(2, Now, null, true, false, false);
        Binding.Begin(3, Now); Binding.RecordVerification(3, Now, false, true, true, true, true);
        Binding.Begin(4, Now); Binding.MarkEffectStarted(4); Binding.RecordEffect(4, Now, null, true, false, false);
        Binding.Begin(5, Now); Binding.RecordVerification(5, Now, true, false, true, true, true);
    }
    public WhatsAppFlowDeprecationService Service() => new(this, this, this, this, UnitOfWork, Clock,
        NullLogger<WhatsAppFlowDeprecationService>.Instance);
    public Task LockSelectionAsync(CancellationToken cancellationToken)
    { Assert.True(UnitOfWork.InTransaction); Locks.Add("Definitions"); return Task.CompletedTask; }
    public Task LockAsync(Guid neutralOperationId, CancellationToken cancellationToken)
    { Assert.True(UnitOfWork.InTransaction); Assert.Equal("Definitions", Locks[^1]); Locks.Add("Flow"); return Task.CompletedTask; }
    public Task<IReadOnlyList<StructuredFormVersionReference>> ListUnusedAsync(IReadOnlyCollection<StructuredFormVersionReference> candidates, CancellationToken cancellationToken)
    { Assert.True(UnitOfWork.InTransaction); return Task.FromResult<IReadOnlyList<StructuredFormVersionReference>>(Unused ? candidates.ToArray() : []); }
    public Task<WhatsAppFlowOperation?> GetAsync(Guid neutralOperationId, CancellationToken cancellationToken) => Task.FromResult<WhatsAppFlowOperation?>(Operation.NeutralOperationId == neutralOperationId ? Operation : null);
    public Task<WhatsAppFlowOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<WhatsAppFlowOperation?>(Operation.Id == id ? Operation : null);
    public Task<IReadOnlyList<WhatsAppFlowBinding>> ListBindingsAsync(Guid operationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WhatsAppFlowBinding>>(operationId == Operation.Id ? [Binding] : []);
    public void Add(WhatsAppFlowOperation operation) => throw new NotSupportedException();
    public void AddBinding(WhatsAppFlowBinding binding) => throw new NotSupportedException();
    public Task<WhatsAppFlowOperationRow?> FindAsync(Guid neutralOperationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> ListDueDeprecationBindingsAsync(DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([Binding.Id]);
    public Task<WhatsAppFlowDeprecationCandidateRow?> FindDeprecationAsync(Guid bindingId, CancellationToken cancellationToken) =>
        Task.FromResult<WhatsAppFlowDeprecationCandidateRow?>(bindingId == Binding.Id ? new(Binding.Id, Operation.Id, Operation.NeutralOperationId, Operation.DefinitionId, Operation.VersionId) : null);
    public Task<bool> ExistsOpenVersionWorkAsync(Guid versionId, DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult(HasOpenWork);
    public byte[] ProtectManifest(Guid operationId, Guid neutralOperationId, Guid versionId, WhatsAppFlowStoredManifest manifest) => throw new NotSupportedException();
    public WhatsAppFlowStoredManifest ReadManifest(Guid operationId, Guid neutralOperationId, Guid versionId, byte[] protectedManifest)
    { Assert.Equal(Operation.Id, operationId); Assert.Equal(Operation.NeutralOperationId, neutralOperationId); Assert.Equal(Operation.VersionId, versionId); if (UnreadableManifest) throw new InvalidOperationException("The protected manifest cannot be read."); return Manifest; }
    public byte[] ProtectProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, WhatsAppFlowProjection projection) => throw new NotSupportedException();
    public WhatsAppFlowProjection ReadProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, byte[] protectedProjection) => throw new NotSupportedException();
}
