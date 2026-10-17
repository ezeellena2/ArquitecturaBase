using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed class FormActionRecipientReader(ApplicationDbContext db):IFormActionRecipientReader
{
    public Task<FormActionRecipientRow?> FindAsync(Guid userId,CancellationToken cancellationToken)=>db.Users.AsNoTracking().Where(user=>user.Id==userId&&user.IsActive).Select(user=>new FormActionRecipientRow(user.Id,user.Culture,user.DisplayName)).SingleOrDefaultAsync(cancellationToken);
    public async Task<IReadOnlyList<FormActionRecipientRow>> ListForRoleAsync(Guid roleId,CancellationToken cancellationToken)=>await db.Users.AsNoTracking()
        .Where(user=>user.IsActive&&db.UserRoles.Any(role=>role.UserId==user.Id&&role.RoleId==roleId)).OrderBy(user=>user.Id).Select(user=>new FormActionRecipientRow(user.Id,user.Culture,user.DisplayName)).ToArrayAsync(cancellationToken);
}
