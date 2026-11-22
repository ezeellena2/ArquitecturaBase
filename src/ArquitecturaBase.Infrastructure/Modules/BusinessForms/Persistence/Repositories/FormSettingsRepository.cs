using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

// RC4-A: the row of Configuración › Formularios, saved under its own lock: two first saves never both insert it, and the second one gets 409.
internal sealed class FormSettingsRepository(ApplicationDbContext dbContext) : IFormSettingsRepository
{
    public Task LockAsync(CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Settings], cancellationToken);

    public Task<BusinessFormsSettings?> GetAsync(CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormsSettings>().SingleOrDefaultAsync(settings => settings.Id == BusinessFormsSettings.SingletonId, cancellationToken);

    public void Add(BusinessFormsSettings settings) => dbContext.Set<BusinessFormsSettings>().Add(settings);
}
