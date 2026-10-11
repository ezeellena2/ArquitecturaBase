using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppFormRecipientReader(ApplicationDbContext dbContext) : IWhatsAppFormRecipientReader
{
    public async Task<WhatsAppFormRecipientRow?> FindAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var row = await (from contact in dbContext.Set<WhatsAppContact>().AsNoTracking().Where(value => value.Id == contactId)
                         join account in dbContext.Users on contact.UserId equals (Guid?)account.Id into accounts
                         from account in accounts.DefaultIfEmpty()
                         select new WhatsAppFormRecipientRow(contact.Id, contact.LinkRevision, contact.WaId, contact.UserIdentifier,
                             account == null ? null : account.Id, account == null ? null : account.DisplayName,
                             account == null ? null : account.Email, account != null && account.IsActive,
                             account == null ? null : account.Culture, account == null ? null : account.ConcurrencyStamp, ""))
                         .SingleOrDefaultAsync(cancellationToken);
        if (row?.UserId is not { } userId) return row;
        var roles = await (from membership in dbContext.UserRoles.AsNoTracking().Where(value => value.UserId == userId)
                           join role in dbContext.Roles on membership.RoleId equals role.Id
                           orderby role.Id
                           select new { role.Id, role.ConcurrencyStamp }).ToArrayAsync(cancellationToken);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", roles.Select(role => $"{role.Id:N}:{role.ConcurrencyStamp}")))));
        return row with { RoleFingerprint = fingerprint };
    }
}
