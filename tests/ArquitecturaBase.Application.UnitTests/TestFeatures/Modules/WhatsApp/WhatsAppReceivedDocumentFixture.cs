using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
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

// Task 8 of the documents: a document received on the assistant's conversation. Meta, the owner's reader, the private copy, the inbox's
// port and the engine's availability are doubles that assert they run outside the transaction; the replies and the handoff go through the
// real policies and outbox. The reader takes a document that waits for its request only once its time passed or a message followed it.
internal sealed class WhatsAppReceivedDocumentFixture : IWhatsAppReceivedDocumentReader, IWhatsAppReceivedDocumentRepository, IWhatsAppMediaClient,
    IStructuredAttachmentReader, IStructuredDocumentChannel, IAssistantOperations
{
    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nA fictitious receipt\n%%EOF\n");
    public WhatsAppAssistantSuggestionFixture Host { get; } = new();
    public MemoryPrivateFileStore Files { get; }
    public WhatsAppMessage Message { get; }
    public byte[] Content { get; }
    public StructuredAttachmentPreparation? Preparation { get; set; }
    public StructuredDocumentLoading Loading { get; set; } = new(true, true);
    public bool MayAuthor { get; set; }
    public bool InstallationReadsDocuments { get; set; } = true;
    public bool FollowedByText { get; set; }
    /// <summary>Task 9: the contiguous text or audio, before or after it, is its request (the reader pairs them as a spreadsheet).</summary>
    public bool PairedRequest { get; set; }
    /// <summary>What the inbox's port answers each time it is asked, in order, before <see cref="Loading"/>: a change between two decisions.</summary>
    public Queue<StructuredDocumentLoading> LoadingAnswers { get; } = new();
    public StructuredDocumentReplyKind? RegistrationReply { get; set; }
    public Error? DownloadError { get; set; }
    public Action? BeforeDownload { get; set; }
    public List<long> DownloadLimits { get; } = [];
    public int Downloads => DownloadLimits.Count;
    public List<string> PreparedNames { get; } = [];
    public List<string> Locks { get; } = [];
    public List<StructuredDocumentRegistrationRequest> Registrations { get; } = [];
    public List<Guid> LoadingAsked { get; } = [];
    public WhatsAppReceivedDocumentDispatchService Dispatch { get; }
    public WhatsAppContact Contact => Host.Context.Contact;
    public DateTime Now => Host.Context.Clock.GetUtcNow().UtcDateTime;
    public IReadOnlyList<WhatsAppOutgoingMessage> Replies => Host.Conversation.Outgoing;

    public WhatsAppReceivedDocumentFixture(byte[]? content = null, bool storage = true, string? metaHash = null, string? caption = "Cargalo en pagos",
        string contentType = "application/pdf", string? fileName = "comprobante.pdf")
    {
        Content = content ?? Pdf;
        Files = new MemoryPrivateFileStore(storage);
        Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        Message = WhatsAppMessage.Inbound(Contact.Id, "wamid.document", WhatsAppMessageKind.ReceivedDocument, caption, null, Now);
        Assert.True(Message.SetDocumentDescriptor("1234", contentType, metaHash ?? Hash(Content), fileName));
        var recipientGuard = new WhatsAppFormActionPolicy(Host.Context.Users, new FakePhoneNumberParser(), new FakeWhatsAppAvailability(true, true), Host.Context.Clock);
        var conversations = new Conversations(this);
        var preparation = new WhatsAppReceivedDocumentPreparationPolicy(this, this, Files, Host.Context.Recipients, recipientGuard, this, this);
        var replies = new WhatsAppReceivedDocumentReplyPolicy(Host.Context.Policy, WhatsAppAssistantAcceptanceFixture.CreateAutomatic(Host), Host.Requests,
            new WhatsAppAssistantHandoffPolicy(conversations, recipientGuard, Host.Context.Clock), Host.Context.Clock);
        var reading = new WhatsAppReceivedDocumentReadingPolicy(Host.Context.Users, Host.Context.Recipients, Host.Context.Settings, new WhatsAppContentRetention(90),
            Host.Context.Clock);
        Dispatch = new(this, this, conversations, preparation, replies, reading, Host.UnitOfWork, NullLogger<WhatsAppReceivedDocumentDispatchService>.Instance);
    }

    public static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public string Key(string extension = ".pdf") => $"whatsapp/documents/{Message.Id:N}{extension}";

    public static StructuredAttachmentPreparation Prepared(StructuredDocumentKind kind, byte[] content, int? pages = null, StructuredDocumentReading? reading = null) =>
        StructuredAttachmentPreparation.Prepared(new StructuredDocument("comprobante", kind, WhatsAppReceivedDocumentPreparationPolicy.MediaType(kind),
            content.LongLength, Hash(content), pages, reading));

    public WhatsAppReceivedDocumentSourceRow Row() => new()
    {
        RequestAwaitedUntilUtc = Message.DocumentRequestAwaitedUntilUtc, FollowedByText = FollowedByText, PairedRequest = PairedRequest,
        MessageId = Message.Id, ContactId = Contact.Id, OccurredAtUtc = Message.OccurredAtUtc, Mode = Contact.Mode, LinkedUserId = Contact.UserId,
        LinkRevision = Contact.LinkRevision, State = Message.DocumentState!.Value, Caption = Message.Body, MediaId = Message.DocumentMediaId,
        ContentType = Message.DocumentContentType, Sha256 = Message.DocumentSha256, FileName = Message.DocumentFileName, StorageKey = Message.DocumentStorageKey,
        Size = Message.DocumentSize, CopySha256 = Message.DocumentCopySha256, Pages = Message.DocumentPages, HasReading = Message.DocumentReadingJson is not null,
        RequestId = Message.DocumentRequestId, ActorUserId = Message.DocumentActorUserId, ContextJson = Message.DocumentContextJson,
        CapturedLinkRevision = Message.DocumentLinkRevision, ContentExpiresAtUtc = Message.DocumentContentExpiresAtUtc,
    };

    public static AssistantOperationResponse Completed(Guid requestId, StructuredDocumentReading reading) =>
        new(requestId, "completed", null, null, null) { DocumentReading = reading };

    public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<bool> ExistsPendingAsync(DateTime nowUtc, CancellationToken ct) => throw new NotSupportedException();

    public Task<WhatsAppReceivedDocumentSourceRow?> FindPendingAsync(Guid contactId, DateTime nowUtc, CancellationToken ct) =>
        Task.FromResult(contactId == Contact.Id && (Message.DocumentState == WhatsAppDocumentState.Reading || Message.DocumentState == WhatsAppDocumentState.Pending
            && (Message.DocumentRequestAwaitedUntilUtc is not { } until || until <= nowUtc || FollowedByText)) ? Row() : null);

    public Task<WhatsAppReceivedDocumentSourceRow?> FindAsync(Guid contactId, Guid messageId, CancellationToken ct) =>
        Task.FromResult(contactId == Contact.Id && messageId == Message.Id ? Row() : null);

    public Task<WhatsAppReceivedDocumentSourceRow?> FindByRequestAsync(Guid contactId, Guid requestId, CancellationToken ct) =>
        Task.FromResult(contactId == Contact.Id && requestId == Message.DocumentRequestId ? Row() : null);

    public Task<IReadOnlyList<Guid>> ListRetainedMessageIdsAsync(IReadOnlyCollection<Guid> messageIds, DateTime cutoffUtc, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<WhatsAppMessage?> GetForUpdateAsync(Guid contactId, Guid messageId, CancellationToken ct)
    {
        Assert.True(Host.UnitOfWork.InTransaction);
        Locks.Add("message");
        return Task.FromResult(contactId == Contact.Id && messageId == Message.Id ? Message : null);
    }

    public Task<WhatsAppMessage?> GetByRequestForUpdateAsync(Guid contactId, Guid requestId, CancellationToken ct)
    {
        Assert.True(Host.UnitOfWork.InTransaction);
        Locks.Add("message");
        return Task.FromResult(contactId == Contact.Id && requestId == Message.DocumentRequestId ? Message : null);
    }

    public Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId, string contentType, string sha256, long maximumBytes, CancellationToken ct)
    {
        Assert.False(Host.UnitOfWork.InTransaction);
        Assert.Equal(("1234", Message.DocumentContentType, Message.DocumentSha256), (mediaId, contentType, sha256));
        DownloadLimits.Add(maximumBytes);
        BeforeDownload?.Invoke();
        return Task.FromResult(DownloadError is { } error ? Result.Failure<WhatsAppMediaContent>(error) : Result.Success(new WhatsAppMediaContent(Content)));
    }

    // A spreadsheet never decides a document's download: only the documents answer does.
    public Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken ct) =>
        Task.FromResult(new StructuredAttachmentAccess(Spreadsheets: true, MayAuthor && actorUserId == Host.Context.Subject.Id));

    public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken ct)
    {
        Assert.False(Host.UnitOfWork.InTransaction);
        PreparedNames.Add(fileName);
        return Task.FromResult(Preparation ?? Prepared(StructuredDocumentKind.Pdf, content, pages: 2));
    }

    public Task<StructuredDocumentLoading> FindLoadingAsync(Guid personUserId, CancellationToken ct)
    {
        LoadingAsked.Add(personUserId);
        var loading = LoadingAnswers.TryDequeue(out var next) ? next : Loading;
        return Task.FromResult(loading with { CanLoad = loading.CanLoad && personUserId == Host.Context.Subject.Id });
    }

    public Task<StructuredDocumentRegistration> RegisterAsync(StructuredDocumentRegistrationRequest request, CancellationToken ct)
    {
        Assert.False(Host.UnitOfWork.InTransaction);
        Registrations.Add(request);
        return Task.FromResult(new StructuredDocumentRegistration(RegistrationReply == StructuredDocumentReplyKind.Unavailable ? null : Guid.NewGuid(),
            RegistrationReply ?? (request.NotRead == StructuredDocumentNotRead.Quota ? StructuredDocumentReplyKind.Quota : StructuredDocumentReplyKind.Rejected)));
    }

    public Task<StructuredDocumentSendPreparation> PrepareSendAsync(StructuredDocumentSendRequest request, CancellationToken ct) => throw new NotSupportedException();

    public Task<StructuredDocumentChoiceState> ChooseOtherAsync(StructuredDocumentChoiceRequest request, CancellationToken ct) => throw new NotSupportedException();

    // A local read without effects: the decision asks it before the download and again under the lock, as the owner's ports.
    public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct) =>
        Task.FromResult(new AssistantAvailabilityResponse("available") { ReadsDocuments = InstallationReadsDocuments });

    public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct) => throw new NotSupportedException();
    public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct) => throw new NotSupportedException();

    public async Task<byte[]?> StoredAsync(string key)
    {
        await using var stream = await Files.OpenReadAsync(key, TestContext.Current.CancellationToken);
        if (stream is null) return null;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
        return copy.ToArray();
    }

    // The contact's lock, recorded before the message's; the outbox is the assistant fixture's.
    private sealed class Conversations(WhatsAppReceivedDocumentFixture host) : IWhatsAppConversationRepository
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
        public Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(DateTime waitingSinceUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoffUtc, CancellationToken ct) => throw new NotSupportedException();
        public void Add(WhatsAppReadState state) => throw new NotSupportedException();
        public void Add(WhatsAppOutgoingMessage outgoing) => host.Host.Conversation.Add(outgoing);
    }
}
