using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssistantQualification;

// Owns a separate DI container that is never started as a host. It registers the real source reader.
internal sealed class SourceConnectionServices(ServiceProvider services) : IDisposable, IAsyncDisposable
{
    public ServiceProvider Services { get; } = services;
    public void Dispose() => Services.Dispose();
    public ValueTask DisposeAsync() => Services.DisposeAsync();
}

// This bridge never resolves or calls a protector, provider client, plaintext key or connection writer.
internal sealed class ReadOnlyAiConnectionReader(SourceConnectionServices source) : IAiConnectionReader
{
    public Task<AiConnectionSnapshot> FindAsync(CancellationToken cancellationToken) =>
        ReadAsync(reader => reader.FindAsync(cancellationToken), cancellationToken);

    public Task<AiConnectionChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) =>
        ReadAsync(reader => reader.FindChangeAsync(operatorId, requestId, cancellationToken), cancellationToken);

    private async Task<T> ReadAsync<T>(Func<IAiConnectionReader, Task<T>> read, CancellationToken cancellationToken)
    {
        await using var scope = source.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        // Reader uses AsNoTracking. The protected snapshot travels only to the real engine.
        // Disposal ends the read transaction; this bridge never commits or saves.
        return await read(scope.ServiceProvider.GetRequiredService<IAiConnectionReader>());
    }
}
