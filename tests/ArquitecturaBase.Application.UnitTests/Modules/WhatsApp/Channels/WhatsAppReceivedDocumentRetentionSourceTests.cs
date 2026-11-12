using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

// Task 8 of the documents: the copy of a received document stays while its message keeps its content. The prefix is the spreadsheets':
// each source takes only its own extensions, so neither keeps the other's copies.
public sealed class WhatsAppReceivedDocumentRetentionSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_the_copies_of_documents_whose_messages_keep_their_content_stay()
    {
        var kept = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var reader = new Reader([kept]);
        var source = new WhatsAppReceivedDocumentRetentionSource(reader, new WhatsAppContentRetention(90), new FakeTimeProvider(Now));
        string[] keys =
        [
            $"whatsapp/documents/{kept:N}.pdf", $"whatsapp/documents/{kept:N}.jpg", $"whatsapp/documents/{kept:N}.png", $"whatsapp/documents/{kept:N}.docx",
            $"whatsapp/documents/{expired:N}.pdf", $"whatsapp/documents/{kept:N}.xlsx", $"whatsapp/documents/{kept:N}.csv", $"whatsapp/audio/{kept:N}.ogg",
            "whatsapp/documents/not-an-id.pdf", $"whatsapp/documents/{kept:N}.jpeg", $"forms/documents/{kept:N}.pdf",
        ];

        var referenced = await source.ListReferencedKeysAsync(keys, TestContext.Current.CancellationToken);

        Assert.Equal(keys[..4], referenced);
        Assert.Equal([kept, expired], reader.Asked);
        Assert.Equal(Now.UtcDateTime.AddDays(-90), reader.Cutoff);
    }

    [Fact]
    public async Task Without_document_keys_it_does_not_read()
    {
        var reader = new Reader([]);
        var source = new WhatsAppReceivedDocumentRetentionSource(reader, new WhatsAppContentRetention(90), new FakeTimeProvider(Now));

        Assert.Empty(await source.ListReferencedKeysAsync([$"whatsapp/documents/{Guid.NewGuid():N}.xlsx"], TestContext.Current.CancellationToken));

        Assert.Null(reader.Asked);
    }

    private sealed class Reader(IReadOnlyList<Guid> retained) : IWhatsAppReceivedDocumentReader
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
        public Task<bool> ExistsPendingAsync(DateTime nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppReceivedDocumentSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppReceivedDocumentSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppReceivedDocumentSourceRow?> FindByRequestAsync(Guid contactId, Guid requestId, CancellationToken ct) => throw new NotSupportedException();
    }
}
