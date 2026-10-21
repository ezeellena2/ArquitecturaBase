using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppFlowPublicationFixture : IWhatsAppFlowRepository, IWhatsAppFlowReader, IWhatsAppFlowEnvironment, IWhatsAppFlowProtector
{
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public List<WhatsAppFlowBinding> Bindings { get; } = [];
    public Dictionary<string, WhatsAppFlowProjection> Projections { get; } = new(StringComparer.Ordinal);
    public int AccountChecks { get; private set; }
    public bool IsConfigured => true;
    public TimeSpan SessionLifetime => TimeSpan.FromHours(24);
    public WhatsAppFlowPublicationService CreateService() => new(this, this, this, UnitOfWork, this, NullLogger<WhatsAppFlowPublicationService>.Instance);
    public Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken cancellationToken)
    {
        Assert.False(UnitOfWork.InTransaction);
        AccountChecks++;
        return Task.FromResult<Result<WhatsAppFlowAccountContext>>(new WhatsAppFlowAccountContext("waba", "phone", "v25.0", "configuration"));
    }
    public Task LockAsync(Guid neutralOperationId, CancellationToken cancellationToken)
    {
        Assert.True(UnitOfWork.InTransaction);
        return Task.CompletedTask;
    }
    public Task<WhatsAppFlowOperation?> GetAsync(Guid neutralOperationId, CancellationToken cancellationToken) => Task.FromResult<WhatsAppFlowOperation?>(null);
    public Task<WhatsAppFlowOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<WhatsAppFlowBinding>> ListBindingsAsync(Guid operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public void Add(WhatsAppFlowOperation operation) => Assert.True(UnitOfWork.InTransaction);
    public void AddBinding(WhatsAppFlowBinding binding)
    {
        Assert.True(UnitOfWork.InTransaction);
        Bindings.Add(binding);
    }
    public Task<WhatsAppFlowOperationRow?> FindAsync(Guid neutralOperationId, CancellationToken cancellationToken) => Task.FromResult<WhatsAppFlowOperationRow?>(null);
    public Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    public byte[] ProtectManifest(Guid operationId, Guid neutralOperationId, Guid versionId, WhatsAppFlowStoredManifest manifest) => [1];
    public WhatsAppFlowStoredManifest ReadManifest(Guid operationId, Guid neutralOperationId, Guid versionId, byte[] protectedManifest) => throw new NotSupportedException();
    public byte[] ProtectProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, WhatsAppFlowProjection projection)
    {
        Projections.Add(culture, projection);
        return [1];
    }
    public WhatsAppFlowProjection ReadProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, byte[] protectedProjection) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> ListDueDeprecationBindingsAsync(DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task<WhatsAppFlowDeprecationCandidateRow?> FindDeprecationAsync(Guid bindingId, CancellationToken cancellationToken) => Task.FromResult<WhatsAppFlowDeprecationCandidateRow?>(null);
    public Task<bool> ExistsOpenVersionWorkAsync(Guid versionId, DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult(true);
}
