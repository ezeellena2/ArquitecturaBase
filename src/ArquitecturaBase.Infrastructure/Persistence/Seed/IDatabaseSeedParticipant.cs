namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>Optional technical seed work inside the existing database seed boundary and lock.</summary>
internal interface IDatabaseSeedParticipant
{
    Task SeedAsync(CancellationToken cancellationToken);
}
