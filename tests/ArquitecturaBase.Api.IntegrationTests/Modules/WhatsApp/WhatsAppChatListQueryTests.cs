using System.Reflection;
using System.Text.RegularExpressions;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// The list of chats reads each fact of a contact's last message in a correlated subquery, and its kind (an audio, a spreadsheet,
// a form reply) only once: a preview that depends on another kind reads it from there instead of adding a subquery.
public sealed partial class WhatsAppChatListQueryTests
{
    [Fact]
    public void The_list_reads_the_kind_of_the_last_message_in_a_single_subquery()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new WhatsAppChatReader(db);
        var query = Assert.IsAssignableFrom<IQueryable>(typeof(WhatsAppChatReader).GetMethod("Chats", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(reader, [null, Guid.NewGuid()]));
        var sql = query.ToQueryString();
        // The body, the purpose, whether the assistant or a form action sent it, the kind and the access event.
        Assert.Equal(6, LastMessage().Count(sql));
    }

    [GeneratedRegex("\"OccurredAtUtc\" DESC", RegexOptions.CultureInvariant)]
    private static partial Regex LastMessage();
}
