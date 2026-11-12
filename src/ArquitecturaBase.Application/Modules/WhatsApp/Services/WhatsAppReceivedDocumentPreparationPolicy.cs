using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// What is done with a received document, decided before any I/O and again under the lock (decision 10): kept as today, the line of an
/// unlinked number, the refusal of an account that can neither load nor build forms, waiting for its request, or downloaded.
/// </summary>
internal enum WhatsAppReceivedDocumentDecision { Keep, NotLinked, NotAllowed, Wait, Download }

// Task 8 of the documents: while no form published in a channel reads documents, a document without a request stays in the chat as
// before, for everybody. With one (or with a request), it is downloaded only for the linked, active account of an automatic conversation,
// whose number is the chat's, that can build forms (the owner lets it attach documents) or load them (the inbox's port), with private
// storage for the copy and, for a PDF or a photo, an installation that reads documents (what Home publishes as documentReading); it will
// be read when it has a request or the person can load it. The request is its caption: the contiguous text or audio is the circuit's
// (HasRequest is the seam), so one that could be asked for waits for it, still pending, until the pairing window passes or another
// message follows. An unlinked number and a linked account that can do neither are not read. Downloading, checking, preparing and
// copying happen outside the channel transaction, as a spreadsheet's: the declared size and the bytes are bounded, the bytes must have
// Meta's SHA-256, the owner prepares them through the neutral port, the copy is written once and read back, and a file that cannot be
// read goes to the inbox discarded: from that copy when the inbox may keep its kind, otherwise with only what the person sent (never the
// bytes of a macro-enabled Word nor a photo over the pixel limit); registering it is retried for a person who loads.
internal sealed class WhatsAppReceivedDocumentPreparationPolicy(IWhatsAppMediaClient media, IStructuredAttachmentReader attachments,
    IPrivateFileStore files, IWhatsAppFormRecipientReader recipients, WhatsAppFormActionPolicy recipientGuard, IStructuredDocumentChannel documents,
    IAssistantOperations operations)
{
    /// <summary>The channel's id in the inbox; the source id is the message's.</summary>
    public const string ChannelId = "whatsapp";
    public const string NotLinkedCode = "notLinked";
    public const string NotAllowedCode = "notAllowed";
    public const string QuotaCode = "quota";

    // The spreadsheets' prefix: each TryParseKey takes only its own extensions.
    private const string Prefix = "whatsapp/documents/";

    public static string Key(Guid messageId, StructuredDocumentKind kind) => $"{Prefix}{messageId:N}{Extension(kind)}";

    private static string Extension(StructuredDocumentKind kind) => kind switch
    {
        StructuredDocumentKind.Pdf => ".pdf",
        StructuredDocumentKind.Jpeg => ".jpg",
        StructuredDocumentKind.Png => ".png",
        _ => ".docx",
    };

    public static bool TryParseKey(string? key, out Guid messageId) => TryParseKey(key, out messageId, out _);

    public static bool TryParseKey(string? key, out Guid messageId, out StructuredDocumentKind kind)
    {
        messageId = Guid.Empty;
        kind = default;
        if (key is null || !key.StartsWith(Prefix, StringComparison.Ordinal) || key.Length <= Prefix.Length + 32) return false;
        var name = key[Prefix.Length..];
        StructuredDocumentKind? parsed = name[32..] switch
        {
            ".pdf" => StructuredDocumentKind.Pdf,
            ".jpg" => StructuredDocumentKind.Jpeg,
            ".png" => StructuredDocumentKind.Png,
            ".docx" => StructuredDocumentKind.Docx,
            _ => null,
        };
        if (parsed is not { } found || !Guid.TryParseExact(name[..32], "N", out messageId) || messageId == Guid.Empty) return false;
        kind = found;
        return true;
    }

    public static string MediaType(StructuredDocumentKind kind) => kind switch
    {
        StructuredDocumentKind.Pdf => "application/pdf",
        StructuredDocumentKind.Jpeg => "image/jpeg",
        StructuredDocumentKind.Png => "image/png",
        _ => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    /// <summary>
    /// The seam of the request: the caption now. The circuit (task 9) adds the contiguous text or audio, before or after it, as the
    /// spreadsheets pair (WhatsAppAssistantSourcePolicy); the wait below gives the one after it the chance to arrive.
    /// </summary>
    public static bool HasRequest(WhatsAppReceivedDocumentSourceRow source) => !string.IsNullOrWhiteSpace(source.Caption);

    // A request may still come: the pairing window of the spreadsheets has not passed and no text of the person (nor an audio already
    // transcribed) followed it; an audio still transcribing keeps it waiting, as a spreadsheet waits for it.
    private static bool MayStillBeAsked(WhatsAppReceivedDocumentSourceRow source, DateTime nowUtc) =>
        !source.FollowedByText && nowUtc < source.OccurredAtUtc + WhatsAppAssistantSourcePolicy.PairingWindow;

    // What only the engine reads, as Meta declares it: a PDF or a photo. A Word is read with what its owner prepares.
    private static bool NeedsEngine(string? contentType) => contentType?.Split(';')[0].Trim().ToLowerInvariant() is "application/pdf" or "image/jpeg" or "image/png";

    public async Task<WhatsAppReceivedDocumentPlan> DecideAsync(WhatsAppReceivedDocumentSourceRow source, DateTime nowUtc, CancellationToken ct)
    {
        // An unlinked number learns nothing and asks nothing of anyone, in either mode: only whether some form reads documents decides the
        // chat's line (DOC4), for the operator who attends it.
        if (source.LinkedUserId is not { } subject)
            return (await documents.FindLoadingAsync(Guid.Empty, ct)).AnyFormReads ? WhatsAppReceivedDocumentPlan.NotLinked : WhatsAppReceivedDocumentPlan.Keep;
        if (source.Mode != WhatsAppConversationMode.Assistant) return WhatsAppReceivedDocumentPlan.Keep;
        var recipient = await recipients.FindAsync(source.ContactId, ct);
        if (recipient is not { IsActive: true } || recipient.UserId != subject || recipient.LinkRevision != source.LinkRevision
            || recipient.Culture is not ("es" or "en") || !await recipientGuard.CanReachAsync(recipient.WaId, subject, ct))
            return WhatsAppReceivedDocumentPlan.Keep;
        var loading = await documents.FindLoadingAsync(subject, ct);
        // Only someone who can load or build forms gets it downloaded; anyone else is told so only while some form reads documents.
        if (!loading.CanLoad && !(await attachments.CanAttachAsync(subject, ct)).Documents)
            return loading.AnyFormReads ? new(WhatsAppReceivedDocumentDecision.NotAllowed, recipient.Culture) : WhatsAppReceivedDocumentPlan.Keep;
        if (!files.IsAvailable) return WhatsAppReceivedDocumentPlan.Keep;
        var engineReads = (await operations.GetAvailabilityAsync(ct)).ReadsDocuments;
        if (!engineReads && NeedsEngine(source.ContentType)) return WhatsAppReceivedDocumentPlan.Keep;
        var requested = HasRequest(source);
        // While no form reads documents, one without a request is not downloaded: it waits for the request that may follow it, and then
        // stays as today.
        if (!loading.AnyFormReads && !requested)
            return MayStillBeAsked(source, nowUtc)
                ? new(WhatsAppReceivedDocumentDecision.Wait, recipient.Culture) { AwaitedUntilUtc = source.OccurredAtUtc + WhatsAppAssistantSourcePolicy.PairingWindow }
                : WhatsAppReceivedDocumentPlan.Keep;
        return new(WhatsAppReceivedDocumentDecision.Download, recipient.Culture)
            { Reads = requested || loading.CanLoad, CanLoad = loading.CanLoad, EngineReads = engineReads };
    }

    public async Task<Result<WhatsAppPreparedDocument>> AcquireAsync(WhatsAppReceivedDocumentSourceRow source, WhatsAppReceivedDocumentPlan plan, CancellationToken ct)
    {
        if (source.MediaId is null || source.ContentType is null || source.Sha256 is null || source.LinkedUserId is not { } sender || plan.Culture is not { } culture)
            return FileErrors.InvalidMetadata;
        var downloaded = await media.DownloadAsync(source.MediaId, source.ContentType, source.Sha256, StructuredAttachmentLimits.MaximumBytes, ct);
        // Over the limit, by the size Meta declares (unknown here) or by the bytes, is the person's reason; there is nothing to copy.
        if (downloaded.IsFailure)
            return downloaded.Error == FileErrors.TooLarge
                ? await RejectAsync(source, plan, sender, culture, StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Unknown, null, null, ct)
                : downloaded.Error;
        var content = downloaded.Value.Content;
        if (content.LongLength > StructuredAttachmentLimits.MaximumBytes)
            return await RejectAsync(source, plan, sender, culture, StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Unknown, content.LongLength,
                null, ct);
        var sha256 = Sha256(content);
        if (sha256 != source.Sha256) return FileErrors.HashMismatch;
        var prepared = await attachments.PrepareAsync(content, source.FileName ?? string.Empty, ct);
        if (prepared.Document is { } document)
        {
            var copied = await CopyAsync(Key(source.MessageId, document.Kind), content, sha256, MediaType(document.Kind), ct);
            return copied.IsFailure ? copied.Error : WhatsAppPreparedDocument.Stored(copied.Value, content.LongLength, sha256, document);
        }
        // A spreadsheet that reached here (a type Meta did not declare as one) is a file this path does not read.
        var rejection = prepared.Spreadsheet is not null ? StructuredAttachmentRejection.Unreadable : prepared.Rejection ?? StructuredAttachmentRejection.Unavailable;
        var fileKind = prepared.Spreadsheet is not null ? StructuredAttachmentFileKind.Spreadsheet : prepared.FileKind ?? StructuredAttachmentFileKind.Unknown;
        if (rejection == StructuredAttachmentRejection.Unavailable) return WhatsAppPreparedDocument.Rejected(rejection, fileKind);
        // An empty file has neither a size nor a copy the inbox could keep.
        return await RejectAsync(source, plan, sender, culture, rejection, fileKind, content.LongLength > 0 ? content.LongLength : null,
            content.LongLength > 0 && Registrable(fileKind, rejection, content) is { } kind ? new KeptCopy(kind, content, sha256) : null, ct);
    }

    // A file that cannot be opened goes to the inbox discarded: from its copy when the inbox may keep its kind (a PDF, a photo that does
    // not open or an empty Word), otherwise with what the person sent and no bytes. The inbox takes the documents of someone who loads
    // them: for that person a registration that did not happen is retried with the usual scale.
    private async Task<Result<WhatsAppPreparedDocument>> RejectAsync(WhatsAppReceivedDocumentSourceRow source, WhatsAppReceivedDocumentPlan plan, Guid sender,
        string culture, StructuredAttachmentRejection rejection, StructuredAttachmentFileKind fileKind, long? size, KeptCopy? kept, CancellationToken ct)
    {
        StructuredDocumentCopy? copy = null;
        if (kept is { } file)
        {
            var copied = await CopyAsync(Key(source.MessageId, file.Kind), file.Content, file.Sha256, MediaType(file.Kind), ct);
            if (copied.IsFailure) return copied.Error;
            copy = new(copied.Value, source.FileName ?? WhatsAppMessage.DocumentName(null, MediaType(file.Kind)), MediaType(file.Kind), file.Content.LongLength,
                file.Sha256, null);
        }
        var declared = DeclaredType(source.ContentType);
        var registered = await documents.RegisterAsync(new StructuredDocumentRegistrationRequest(ChannelId, source.MessageId.ToString("N"), sender, culture,
            source.OccurredAtUtc, copy)
        {
            NotRead = StructuredDocumentNotRead.Rejected, Rejection = rejection,
            File = copy is null ? new StructuredDocumentFile(source.FileName ?? WhatsAppMessage.DocumentName(null, declared), declared, size) : null,
        }, ct);
        if (registered.Kind == StructuredDocumentReplyKind.Unavailable && plan.CanLoad) return FileErrors.Unavailable;
        return WhatsAppPreparedDocument.Rejected(rejection, fileKind);
    }

    // The media type as Meta declared it, without its parameters; a generic one when it has none or the owner could not keep it.
    private static string DeclaredType(string? contentType) =>
        contentType?.Split(';')[0].Trim().ToLowerInvariant() is { Length: > 0 and <= StructuredDocumentFile.MaxMediaTypeLength } type ? type
            : "application/octet-stream";

    private sealed record KeptCopy(StructuredDocumentKind Kind, byte[] Content, string Sha256)
    {
        public override string ToString() => "KeptCopy { Content = [REDACTED] }";
    }

    /// <summary>
    /// The day's reading quota (decision 11): the stored document goes to the inbox unclassified, from its copy, before its state is final;
    /// registering again answers the same. Saying so is the circuit's (task 9).
    /// </summary>
    public Task<StructuredDocumentRegistration> RegisterOverQuotaAsync(WhatsAppReceivedDocumentSourceRow source, string culture, CancellationToken ct) =>
        source is { StorageKey: { } key, Size: { } size, CopySha256: { } sha256, LinkedUserId: { } sender } && TryParseKey(key, out _, out var kind)
            ? documents.RegisterAsync(new StructuredDocumentRegistrationRequest(ChannelId, source.MessageId.ToString("N"), sender, culture, source.OccurredAtUtc,
                new(key, source.FileName ?? WhatsAppMessage.DocumentName(null, MediaType(kind)), MediaType(kind), size, sha256,
                    kind == StructuredDocumentKind.Pdf ? source.Pages : null)) { NotRead = StructuredDocumentNotRead.Quota }, ct)
            : Task.FromResult(new StructuredDocumentRegistration(null, StructuredDocumentReplyKind.Unavailable));

    // What the inbox can keep of a file that is not read: a PDF (protected, too many pages or unreadable), a photo that does not open
    // or an empty Word, by what the bytes are. A Word older than 2007, one with macros or anything else is not a document it shows, and
    // a photo over the pixel limit is never kept: a reviewer's browser would decode it.
    private static StructuredDocumentKind? Registrable(StructuredAttachmentFileKind fileKind, StructuredAttachmentRejection rejection, byte[] content) =>
        fileKind switch
        {
            StructuredAttachmentFileKind.Pdf => StructuredDocumentKind.Pdf,
            StructuredAttachmentFileKind.Image when rejection == StructuredAttachmentRejection.TooLarge => null,
            StructuredAttachmentFileKind.Image when content.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) => StructuredDocumentKind.Jpeg,
            StructuredAttachmentFileKind.Image when content.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) => StructuredDocumentKind.Png,
            StructuredAttachmentFileKind.Word when rejection == StructuredAttachmentRejection.Empty => StructuredDocumentKind.Docx,
            _ => null,
        };

    // The copy is written once and read back: another host, or an attempt that did not commit, may have written it first.
    private async Task<Result<string>> CopyAsync(string key, byte[] content, string sha256, string mediaType, CancellationToken ct)
    {
        await using (var stream = new MemoryStream(content, false)) await files.WriteOnceAsync(key, stream, mediaType, ct);
        await using var stored = await files.OpenReadAsync(key, ct);
        if (stored is null) return FileErrors.Unavailable;
        var copy = await ReadBoundedAsync(stored, ct);
        return copy is not null && Sha256(copy) == sha256 ? key : FileErrors.HashMismatch;
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, ct);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > StructuredAttachmentLimits.MaximumBytes) return null;
            buffer.Write(chunk, 0, count);
        }
    }

    private static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}

/// <summary>What is done with a received document: the decision and the language of the person, for the inbox and the replies.</summary>
internal sealed record WhatsAppReceivedDocumentPlan(WhatsAppReceivedDocumentDecision Decision, string? Culture = null)
{
    public static WhatsAppReceivedDocumentPlan Keep { get; } = new(WhatsAppReceivedDocumentDecision.Keep);
    public static WhatsAppReceivedDocumentPlan NotLinked { get; } = new(WhatsAppReceivedDocumentDecision.NotLinked);

    /// <summary>Read once it is stored: it has a request or the person can load it.</summary>
    public bool Reads { get; init; }

    /// <summary>The person loads documents: the inbox takes theirs, so a registration that did not happen is retried.</summary>
    public bool CanLoad { get; init; }

    /// <summary>The installation reads a PDF or a photo (ReadDocument).</summary>
    public bool EngineReads { get; init; }

    /// <summary>Until when it waits for its request (Wait).</summary>
    public DateTime? AwaitedUntilUtc { get; init; }
}

/// <summary>
/// What came of downloading a document: its private copy, with the size and SHA-256 of its bytes and what its owner prepared, or the
/// reason it is not read and what file it was.
/// </summary>
internal sealed record WhatsAppPreparedDocument(string? StorageKey, long Size, string? Sha256, StructuredDocument? Document,
    StructuredAttachmentRejection? Rejection, StructuredAttachmentFileKind FileKind)
{
    public static WhatsAppPreparedDocument Stored(string storageKey, long size, string sha256, StructuredDocument document) =>
        new(storageKey, size, sha256, document, null, StructuredAttachmentFileKind.Unknown);

    public static WhatsAppPreparedDocument Rejected(StructuredAttachmentRejection rejection, StructuredAttachmentFileKind fileKind) =>
        new(null, 0, null, null, rejection, fileKind);

    public override string ToString() => Rejection is { } rejection
        ? $"WhatsAppPreparedDocument {{ Rejection = {rejection}, FileKind = {FileKind} }}" : "WhatsAppPreparedDocument { Content = [REDACTED] }";
}
