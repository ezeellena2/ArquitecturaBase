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

    public async Task<IReadOnlyList<WhatsAppAssistantTestRecipientRow>> ListTestRecipientsAsync(string? search, int limit, CancellationToken cancellationToken)
    {
        var rows = from contact in db.Set<WhatsAppContact>().AsNoTracking()
                   join account in db.Users.AsNoTracking() on contact.UserId equals (Guid?)account.Id
                   where account.IsActive
                   select new { contact.Id, account.DisplayName, account.Email };
        if (search is not null)
        {
            var pattern = LikePatterns.Contains(search);
            rows = rows.Where(row => EF.Functions.ILike(row.DisplayName ?? "", pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(row.Email ?? "", pattern, LikePatterns.EscapeCharacter));
        }

        return await rows.OrderBy(row => row.DisplayName).ThenBy(row => row.Id).Take(limit)
            .Select(row => new WhatsAppAssistantTestRecipientRow(row.Id, row.DisplayName ?? row.Email ?? "", row.Email))
            .ToListAsync(cancellationToken);
    }
}
