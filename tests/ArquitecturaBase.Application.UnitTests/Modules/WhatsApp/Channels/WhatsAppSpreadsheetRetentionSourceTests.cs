using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

// Stage 2: the copy of a received spreadsheet stays while its message keeps its content; other keys are not this source's.
public sealed class WhatsAppSpreadsheetRetentionSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_the_copies_of_messages_that_keep_their_content_stay()
    {
        var kept = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var reader = new Reader([kept]);
        var source = new WhatsAppSpreadsheetRetentionSource(reader, new WhatsAppContentRetention(90), new FakeTimeProvider(Now));
        string[] keys =
        [
            $"whatsapp/documents/{kept:N}.xlsx", $"whatsapp/documents/{kept:N}.csv", $"whatsapp/documents/{expired:N}.csv",
            $"whatsapp/audio/{kept:N}.ogg", $"whatsapp/documents/{kept:N}.pdf", "whatsapp/documents/not-an-id.csv",
            $"ai/assistant/attachments/{kept:N}.xlsx",
        ];

        var referenced = await source.ListReferencedKeysAsync(keys, TestContext.Current.CancellationToken);

        Assert.Equal([$"whatsapp/documents/{kept:N}.xlsx", $"whatsapp/documents/{kept:N}.csv"], referenced);
        Assert.Equal([kept, expired], reader.Asked);
        Assert.Equal(Now.UtcDateTime.AddDays(-90), reader.Cutoff);
    }

    [Fact]
    public async Task Without_spreadsheet_keys_it_does_not_read()
    {
        var reader = new Reader([]);
        var source = new WhatsAppSpreadsheetRetentionSource(reader, new WhatsAppContentRetention(90), new FakeTimeProvider(Now));

        Assert.Empty(await source.ListReferencedKeysAsync([$"whatsapp/audio/{Guid.NewGuid():N}.ogg"], TestContext.Current.CancellationToken));

        Assert.Null(reader.Asked);
    }

    private sealed class Reader(IReadOnlyList<Guid> retained) : IWhatsAppSpreadsheetReader
    {
        public IReadOnlyCollection<Guid>? Asked { get; private set; }
        public DateTime? Cutoff { get; private set; }

        public Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, DateTime cutoffUtc, CancellationToken ct)
        {
            Asked = messageIds;
            Cutoff = cutoffUtc;
            return Task.FromResult<IReadOnlyList<Guid>>([.. retained.Where(messageIds.Contains)]);
        }

        public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<WhatsAppSpreadsheetSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppSpreadsheetSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct) => throw new NotSupportedException();
    }
}
