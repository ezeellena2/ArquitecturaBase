using System.Security.Cryptography;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// Stage 3: a Home photo or PDF is read before its turn, like a recording. Its private copy lives under a key the server derives from its
// request, so its retention keeps it while the reading or a turn that holds it keeps its content. The owner says who may attach a
// document and prepares it through the neutral port; a Word goes with its turn by file-turns, read on the server. A second host may
// win WriteOnce: the copy is re-read, never assumed ours.
internal sealed class AiAssistantHomeDocumentPolicy(IPrivateFileStore files, IStructuredAttachmentReader reader, AiAssistantAccessGuard guard,
    IAiConnectionReader connections)
{
    private const string Prefix = "ai/assistant/documents/";

    public static string Key(Guid requestId, string mediaType) => $"{Prefix}{requestId:N}{Extension(mediaType)}";

    public static bool TryParseKey(string? key, out Guid requestId)
    {
        requestId = Guid.Empty;
        if (key is null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var name = key[Prefix.Length..];
        return name.Length > 32 && name[32..] is ".pdf" or ".jpg" or ".png" && Guid.TryParseExact(name[..32], "N", out requestId) && requestId != Guid.Empty;
    }

    // Before the file is read: the installation reads documents (a qualified profile and private storage), and only then the owner says
    // whether this person may attach them.
    public async Task<Error?> CheckAsync(Guid userId, CancellationToken ct) =>
        !guard.ReadsDocuments(await connections.FindAsync(ct)) ? FieldErrors.On(AiAssistantErrors.AttachmentDocumentUnavailable, "file")
            : !(await reader.CanAttachAsync(userId, ct)).Documents ? AiAssistantErrors.AttachmentDocumentForbidden : null;

    // Only a PDF or a photo is read here: a spreadsheet or a Word goes with its turn by file-turns, and anything else is none of them. A PDF
    // or a photo that is not read says why, naming what it is.
    public async Task<Result<StructuredDocument>> PrepareAsync(byte[] content, string name, CancellationToken ct)
    {
        var prepared = await reader.PrepareAsync(content, name, ct);
        if (prepared.Document is { } document && AiAssistantDocumentPolicy.MediaTypes.Contains(document.MediaType, StringComparer.Ordinal)) return document;
        return prepared is { Rejection: not null, FileKind: StructuredAttachmentFileKind.Pdf or StructuredAttachmentFileKind.Image }
            ? AiAssistantHomeAttachmentPolicy.Rejected(prepared) : FieldErrors.On(AiAssistantErrors.Invalid, "file");
    }

    // The descriptor the engine reads, taken from the bytes: the person's file name, the type the owner detected, the size, the SHA-256 and
    // the pages (a photo is one). It is checked before the copy is kept.
    public static Result<AssistantDocumentInput> Describe(Guid requestId, byte[] content, string name, StructuredDocument document, DateTime expiresAtUtc)
    {
        var descriptor = new AssistantDocumentInput(Key(requestId, document.MediaType), name, document.MediaType, content.LongLength, Sha256(content),
            document.Pages ?? 1, expiresAtUtc);
        return AiAssistantDocumentPolicy.Validate(descriptor) is null ? descriptor : FieldErrors.On(AiAssistantErrors.Invalid, "file");
    }

    // Whether an upload is the file a reading was accepted with: the same size, name and SHA-256, the size first.
    public static bool IsSameFile(AssistantDocumentInput descriptor, byte[] content, string name) =>
        descriptor.Size == content.LongLength && descriptor.Name == name && descriptor.Sha256 == Sha256(content);

    // A copy that cannot be read back says so under the file, in neutral words; another content under the same request is a conflict.
    public async Task<Result> StoreAsync(AssistantDocumentInput descriptor, byte[] content, CancellationToken ct)
    {
        if (!files.IsAvailable) return FieldErrors.On(AiAssistantErrors.AttachmentDocumentNotStored, "file");
        await using (var stream = new MemoryStream(content, false)) await files.WriteOnceAsync(descriptor.StorageKey, stream, descriptor.ContentType, ct);
        await using var stored = await files.OpenReadAsync(descriptor.StorageKey, ct);
        if (stored is null) return FieldErrors.On(AiAssistantErrors.AttachmentDocumentNotStored, "file");
        using var copy = new MemoryStream();
        await stored.CopyToAsync(copy, ct);
        return copy.Length == descriptor.Size && Sha256(copy.GetBuffer().AsSpan(0, (int)copy.Length)) == descriptor.Sha256
            ? Result.Success() : AiAssistantErrors.RequestReused;
    }

    // The file a turn takes from a completed reading: the copy of that request with the whole reading, fitted to the documents' budget.
    // Null when the descriptor does not name that copy or the document cannot be attached.
    public static AssistantAttachmentInput? Attachment(Guid requestId, AssistantDocumentInput descriptor, StructuredDocumentReading reading,
        DateTime expiresAtUtc)
    {
        if (descriptor.StorageKey != Key(requestId, descriptor.ContentType)) return null;
        var kind = descriptor.ContentType switch { "image/jpeg" => StructuredDocumentKind.Jpeg, "image/png" => StructuredDocumentKind.Png, _ => StructuredDocumentKind.Pdf };
        return AiAssistantAttachmentPolicy.ForDocument(descriptor.StorageKey, descriptor.Name, expiresAtUtc, new StructuredDocument(descriptor.Name, kind,
            descriptor.ContentType, descriptor.Size, descriptor.Sha256, kind == StructuredDocumentKind.Pdf ? descriptor.Pages : null, reading));
    }

    // What the person sees of a completed reading: the file, its kind and pages, and how readable it was.
    public static AiAssistantReadDocumentResponse Response(AssistantDocumentInput descriptor, StructuredDocumentReading reading) => new(descriptor.Name,
        descriptor.ContentType switch { "image/jpeg" => "jpeg", "image/png" => "png", _ => "pdf" }, descriptor.Pages, descriptor.Size,
        reading.Readability.ToString().ToLowerInvariant(), reading.Problem?.ToString().ToLowerInvariant());

    private static string Extension(string mediaType) => mediaType switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "Home reads only a PDF or a photo."),
    };

    private static string Sha256(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
