using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

// Stage 2 of the assistant authoring: a spreadsheet received on the assistant's conversation fixture. Meta, the owner's reader and
// the private copy are doubles that assert they run outside the transaction; the reply goes through the real automatic policy.
internal sealed class WhatsAppSpreadsheetFixture : IWhatsAppSpreadsheetReader, IWhatsAppSpreadsheetRepository, IWhatsAppMediaClient, IStructuredAttachmentReader
{
    public static readonly byte[] Csv = Encoding.UTF8.GetBytes("Código,Texto\nA1,Uno\nA2,Dos\n");
    public WhatsAppAssistantSuggestionFixture Host { get; } = new();
    public MemoryPrivateFileStore Files { get; }
    public WhatsAppMessage Message { get; }
    public byte[] Content { get; }
    public StructuredAttachmentPreparation? Preparation { get; set; }
    public StructuredSpreadsheetKind Kind { get; set; } = StructuredSpreadsheetKind.Csv;
    public bool MayAttach { get; set; } = true;
    public Error? DownloadError { get; set; }
    public Action? BeforeDownload { get; set; }
    public List<long> DownloadLimits { get; } = [];
    public int Downloads => DownloadLimits.Count;
    public List<string> PreparedNames { get; } = [];
    // VC1-A: the sheet prepared to load its rows, whether the person loads (the inbox decides) and what the inbox registered.
    public List<string> RowsPreparedNames { get; } = [];
    public StructuredAttachmentPreparation? RowsPreparation { get; set; }
    public StructuredDocumentLoading Loading { get; set; } = StructuredDocumentLoading.None;
    public List<StructuredDocumentRegistrationRequest> Registrations { get; } = [];
    public List<string> Locks { get; } = [];
    public WhatsAppSpreadsheetDispatchService Dispatch { get; }
    public WhatsAppContact Contact => Host.Context.Contact;
    public DateTime Now => Host.Context.Clock.GetUtcNow().UtcDateTime;

    public WhatsAppSpreadsheetFixture(byte[]? content = null, bool storage = true, string? metaHash = null)
    {
        Content = content ?? Csv;
        Files = new MemoryPrivateFileStore(storage);
        Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        Message = WhatsAppMessage.Inbound(Contact.Id, "wamid.spreadsheet", WhatsAppMessageKind.Spreadsheet, "Armame un catálogo con esta planilla", null, Now);
        Assert.True(Message.SetSpreadsheetDescriptor("1234", "text/csv", metaHash ?? Hash(Content), "clientes.csv"));
        var recipientGuard = new WhatsAppFormActionPolicy(Host.Context.Users, new FakePhoneNumberParser(), new FakeWhatsAppAvailability(true, true), Host.Context.Clock);
        var preparation = new WhatsAppSpreadsheetPreparationPolicy(this, this, Files, Host.Context.Recipients, recipientGuard,
            new Documents(this));
        var replies = new WhatsAppSpreadsheetReplyPolicy(Host.Context.Policy, WhatsAppAssistantAcceptanceFixture.CreateAutomatic(Host),
            Host.Requests,
            new WhatsAppReceivedDocumentReplyPolicy(Host.Context.Policy, WhatsAppAssistantAcceptanceFixture.CreateAutomatic(Host), Host.Requests,
                new WhatsAppAssistantHandoffPolicy(new Conversations(this), recipientGuard, Host.Context.Clock), Host.Context.Clock), Host.Context.Clock);
        Dispatch = new(this, this, new Conversations(this), preparation, replies, Host.UnitOfWork, Host.Context.Clock,
            NullLogger<WhatsAppSpreadsheetDispatchService>.Instance);
    }

    public static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public string Key => $"whatsapp/documents/{Message.Id:N}{(Kind == StructuredSpreadsheetKind.Csv ? ".csv" : ".xlsx")}";

    public IReadOnlyList<WhatsAppOutgoingMessage> Replies => Host.Conversation.Outgoing;

    public WhatsAppSpreadsheetSourceRow Row() => new()
    {
        MessageId = Message.Id, ContactId = Contact.Id, OccurredAtUtc = Message.OccurredAtUtc, Mode = Contact.Mode, LinkedUserId = Contact.UserId,
        LinkRevision = Contact.LinkRevision, State = Message.SpreadsheetState!.Value, MediaId = Message.SpreadsheetMediaId,
        ContentType = Message.SpreadsheetContentType, Sha256 = Message.SpreadsheetSha256, FileName = Message.SpreadsheetFileName,
    };

    public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<bool> ExistsPendingAsync(DateTime nowUtc, CancellationToken ct) => throw new NotSupportedException();

    public Task<WhatsAppSpreadsheetSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct) =>
        Task.FromResult(contactId == Contact.Id && Message.SpreadsheetState == WhatsAppSpreadsheetState.Pending ? Row() : null);

    public Task<WhatsAppSpreadsheetSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct) =>
        Task.FromResult(contactId == Contact.Id && messageId == Message.Id ? Row() : null);

    public Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, DateTime cutoffUtc, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<WhatsAppMessage?> GetForUpdateAsync(Guid contactId, Guid messageId, CancellationToken ct)
    {
        Assert.True(Host.UnitOfWork.InTransaction);
        Locks.Add("message");
        return Task.FromResult(contactId == Contact.Id && messageId == Message.Id ? Message : null);
    }

    public Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId, string contentType, string sha256, long maximumBytes, CancellationToken ct)
    {
        Assert.False(Host.UnitOfWork.InTransaction);
        Assert.Equal(("1234", "text/csv", Message.SpreadsheetSha256), (mediaId, contentType, sha256));
        DownloadLimits.Add(maximumBytes);
        BeforeDownload?.Invoke();
        return Task.FromResult(DownloadError is { } error ? Result.Failure<WhatsAppMediaContent>(error) : Result.Success(new WhatsAppMediaContent(Content)));
    }

    // Documents never decide a spreadsheet download: only the spreadsheet answer does.
    public Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken ct) =>
        Task.FromResult(new StructuredAttachmentAccess(MayAttach && actorUserId == Host.Context.Subject.Id, Documents: true));

    public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken ct)
    {
        Assert.False(Host.UnitOfWork.InTransaction);
        PreparedNames.Add(fileName);
        return Task.FromResult(Preparation ?? StructuredAttachmentPreparation.Prepared(new StructuredSpreadsheet(fileName, Kind,
            Kind == StructuredSpreadsheetKind.Csv ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            content.LongLength, Hash(content), Kind == StructuredSpreadsheetKind.Csv ? "" : "Hoja1", 3, 2,
            [new(1, new Dictionary<string, string> { ["A"] = "Código", ["B"] = "Texto" }), new(2, new Dictionary<string, string> { ["A"] = "A1", ["B"] = "Uno" })])));
    }

    public Task<StructuredAttachmentPreparation> PrepareRowsAsync(byte[] content, string fileName, CancellationToken ct)
    {
        Assert.False(Host.UnitOfWork.InTransaction);
        RowsPreparedNames.Add(fileName);
        return Task.FromResult(RowsPreparation ?? StructuredAttachmentPreparation.Prepared(new StructuredSpreadsheet(fileName, StructuredSpreadsheetKind.Csv, "text/csv",
            content.LongLength, Hash(content), "", 3, 2, [new(1, new Dictionary<string, string> { ["A"] = "Código", ["B"] = "Texto" })]) { DataRowCount = 2 }));
    }

    public async Task<byte[]?> StoredAsync(string key)
    {
        await using var stream = await Files.OpenReadAsync(key, TestContext.Current.CancellationToken);
        if (stream is null) return null;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
        return copy.ToArray();
    }

    // The contact's lock, recorded before the message's; the outbox is the assistant fixture's.
    private sealed class Conversations(WhatsAppSpreadsheetFixture host) : IWhatsAppConversationRepository
    {
        public Task<WhatsAppContact?> GetForUpdateAsync(Guid contactId, CancellationToken ct)
        {
            Assert.True(host.Host.UnitOfWork.InTransaction);
            host.Locks.Add("contact");
            return host.Host.Conversation.GetForUpdateAsync(contactId, ct);
        }

        public Task<WhatsAppReadState?> GetReadStateAsync(Guid contactId, Guid operatorUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid contactId, Guid operatorUserId, Guid requestId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid outgoingId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsUnresolvedOutgoingAsync(Guid contactId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppAssistantRequest>> ListPendingAutomaticRequestsAsync(Guid contactId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsStartedAssistantMessageAsync(Guid contactId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoffUtc, CancellationToken ct) => throw new NotSupportedException();
        public void Add(WhatsAppReadState state) => throw new NotSupportedException();
        public void Add(WhatsAppOutgoingMessage outgoing) => host.Host.Conversation.Add(outgoing);
    }

    // VC1-A: the inbox's answer about loading, and what it registers, outside the transaction.
    private sealed class Documents(WhatsAppSpreadsheetFixture host) : IStructuredDocumentChannel
    {
        public Task<StructuredDocumentLoading> FindLoadingAsync(Guid personUserId, CancellationToken ct) =>
            Task.FromResult(personUserId == host.Host.Context.Subject.Id ? host.Loading : StructuredDocumentLoading.None);

        public Task<StructuredDocumentRegistration> RegisterAsync(StructuredDocumentRegistrationRequest request, CancellationToken ct)
        {
            Assert.False(host.Host.UnitOfWork.InTransaction);
            host.Registrations.Add(request);
            return Task.FromResult(new StructuredDocumentRegistration(null, StructuredDocumentReplyKind.Unavailable));
        }

        public Task<StructuredDocumentSendPreparation> PrepareSendAsync(StructuredDocumentSendRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<StructuredDocumentChoiceState> ChooseOtherAsync(StructuredDocumentChoiceRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
}
