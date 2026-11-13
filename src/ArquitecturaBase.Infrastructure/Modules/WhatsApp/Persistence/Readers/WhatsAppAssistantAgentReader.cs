using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppAssistantAgentReader(ApplicationDbContext db) : IWhatsAppAssistantAgentReader
{
    public Task<WhatsAppAssistantAgentRow?> FindMainAsync(CancellationToken cancellationToken) =>
        db.Set<WhatsAppAssistantAgent>().AsNoTracking().Where(agent => agent.IsMain)
            .Select(agent => new WhatsAppAssistantAgentRow(agent.Revision, agent.OriginalCulture, agent.Profile, agent.Cases, agent.Fallback, agent.Limits))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<WhatsAppAssistantTestRecipientRow>> ListTestRecipientsAsync(string? search, bool withEmail, int limit,
        CancellationToken cancellationToken)
    {
        var accounts = from contact in db.Set<WhatsAppContact>().AsNoTracking()
                       join account in db.Users.AsNoTracking() on contact.UserId equals (Guid?)account.Id
                       where account.IsActive
                       select new { contact.Id, account.DisplayName, account.Email, contact.ProfileName, Phone = contact.WaId == null ? null : "+" + contact.WaId };
        // Without the permission to read accounts the email is neither searched nor shown, nor does it name an account without a name:
        // the chat's WhatsApp name or its number do, as in the inbox.
        var rows = withEmail
            ? accounts.Select(row => new { row.Id, Name = row.DisplayName ?? row.Email ?? row.ProfileName ?? row.Phone, row.Email })
            : accounts.Select(row => new { row.Id, Name = row.DisplayName ?? row.ProfileName ?? row.Phone, Email = (string?)null });
        if (search is not null)
        {
            var pattern = LikePatterns.Contains(search);
            rows = rows.Where(row => EF.Functions.ILike(row.Name ?? "", pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(row.Email ?? "", pattern, LikePatterns.EscapeCharacter));
        }

        return await rows.OrderBy(row => row.Name).ThenBy(row => row.Id).Take(limit)
            .Select(row => new WhatsAppAssistantTestRecipientRow(row.Id, row.Name ?? "", row.Email))
            .ToListAsync(cancellationToken);
    }
}
