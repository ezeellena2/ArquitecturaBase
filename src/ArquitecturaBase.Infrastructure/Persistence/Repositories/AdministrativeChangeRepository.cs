using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class AdministrativeChangeRepository(ApplicationDbContext dbContext) : IAdministrativeChangeRepository
{
    public void Add(AdministrativeChange change) => dbContext.AdministrativeChanges.Add(change);
}
