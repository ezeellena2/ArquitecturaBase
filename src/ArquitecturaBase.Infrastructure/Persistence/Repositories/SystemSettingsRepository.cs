using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class SystemSettingsRepository(ApplicationDbContext dbContext) : ISystemSettingsRepository
{
    public Task<SystemSettings?> GetAsync(CancellationToken cancellationToken) =>
        dbContext.SystemSettings.FirstOrDefaultAsync(cancellationToken);

    public void Add(SystemSettings settings) => dbContext.SystemSettings.Add(settings);

    public async Task LockAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Settings locking requires a transaction.");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SystemSettings\" WHERE \"Id\" = {SystemSettings.SingletonId} FOR UPDATE", cancellationToken);
    }
}
