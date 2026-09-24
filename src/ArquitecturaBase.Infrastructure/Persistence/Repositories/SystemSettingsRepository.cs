using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class SystemSettingsRepository(ApplicationDbContext dbContext) : ISystemSettingsRepository
{
    public Task<SystemSettings?> GetAsync(CancellationToken cancellationToken) =>
        dbContext.SystemSettings.FirstOrDefaultAsync(cancellationToken);

    public void Add(SystemSettings settings) => dbContext.SystemSettings.Add(settings);
}
