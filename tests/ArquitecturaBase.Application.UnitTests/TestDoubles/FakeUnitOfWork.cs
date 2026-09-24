using ArquitecturaBase.Application.Interfaces.Persistence;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;
        return Task.FromResult(1);
    }
}
