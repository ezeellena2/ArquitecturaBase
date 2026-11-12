using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Task 8 of the documents: what WhatsApp says about a received document before it is read, as the rejection of a spreadsheet does. A
// file that cannot be read keeps its reason and gets its fixed text of DOC4 once; a linked account that can neither load nor build forms
// is told so and the conversation goes to a person (Derivar, AS4). Each answer is an automatic reference of its own on the message that
// names its own request as the proposal, so the reader leaves it out of the conversation, accepted in the same outbox. Only in the
// person's automatic conversation, in their language and within the 24-hour window; it runs in the caller's transaction.
internal sealed class WhatsAppReceivedDocumentReplyPolicy(WhatsAppAssistantAccessPolicy access, WhatsAppAssistantAutomaticPolicy automatic,
    IWhatsAppAssistantRepository requests, WhatsAppAssistantHandoffPolicy handoffs, TimeProvider clock)
{
    /// <summary>The code the contact keeps when its document is handed to a person.</summary>
    public const string HandoffReasonCode = "documentNotAllowed";

    public async Task RejectAsync(WhatsAppContact contact, WhatsAppMessage message, StructuredAttachmentRejection rejection,
        StructuredAttachmentFileKind fileKind, CancellationToken ct)
    {
        if (!message.RejectDocument(ReasonCode(rejection))) return;
        if (await ReferenceAsync(contact, message, ct) is not { } reference) return;
        var (row, culture) = reference;
        // A fixed text answers no proposal of the model: the reference names its own request.
        if (automatic.AcceptTerminal(contact, row, new WhatsAppAssistantTerminalResponse(row.RequestId, Text(rejection, fileKind, culture), culture)))
            requests.Add(row);
    }

    /// <summary>
    /// A linked account that can neither load nor build forms: the document is not read, the person is told so once and the conversation
    /// goes to Atención manual marked «Necesita una persona», with why for the operator, also when the message cannot go out.
    /// </summary>
    public async Task RefuseAsync(WhatsAppContact contact, WhatsAppMessage message, Guid subject, string personCulture, CancellationToken ct)
    {
        if (message.DocumentState != WhatsAppDocumentState.Pending) return;
        message.SetDocumentUnavailable(WhatsAppReceivedDocumentPreparationPolicy.NotAllowedCode);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        if (await ReferenceAsync(contact, message, ct) is not { } reference)
        {
            // Without the person's conversation (out of the window) nobody is told, and a person still takes the conversation.
            await handoffs.HandOffAsync(contact, subject, Guid.CreateVersion7(), HandoffReasonCode, AssistantTerminalTexts.Get("DocumentNotAllowedReason", personCulture),
                null, ct);
            return;
        }
        var (row, culture) = reference;
        requests.Add(row);
        var outgoing = await handoffs.HandOffAsync(contact, row.SubjectUserId, row.RequestId, HandoffReasonCode,
            AssistantTerminalTexts.Get("DocumentNotAllowedReason", culture), AssistantTerminalTexts.Get("DocumentNotAllowed", culture), ct);
        if (outgoing is null)
        {
            if (!row.FinishWithoutReply(false, nowUtc)) throw new InvalidOperationException("A refused document could not finish its reference.");
            return;
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.RequestId.ToString("N") + row.RequestId.ToString("N"))));
        if (!row.Ready(row.RequestId, null, nowUtc) || !row.Accept(outgoing.Id, fingerprint, null, nowUtc))
            throw new InvalidOperationException("A refused document could not be answered.");
    }

    /// <summary>
    /// The reason kept with the message, as the inbox and the chat name it: protected, a legacy or macro-enabled Word, over the size, too
    /// many pages, empty or unreadable.
    /// </summary>
    public static string ReasonCode(StructuredAttachmentRejection rejection) => rejection switch
    {
        StructuredAttachmentRejection.Legacy => "legacy",
        StructuredAttachmentRejection.Protected => "protected",
        StructuredAttachmentRejection.MacroEnabled => "macroEnabled",
        StructuredAttachmentRejection.TooLarge => "tooLarge",
        StructuredAttachmentRejection.TooManyPages => "tooManyPages",
        StructuredAttachmentRejection.Empty => "empty",
        StructuredAttachmentRejection.Unreadable => "unreadable",
        _ => "unavailable",
    };

    // The person's automatic reference on the message, with the context the assistant would take; none when it already has one or the
    // person cannot be answered here.
    private async Task<(WhatsAppAssistantRequest Row, string Culture)?> ReferenceAsync(WhatsAppContact contact, WhatsAppMessage message, CancellationToken ct)
    {
        var captured = await access.CaptureAsync(contact, null, ct);
        if (captured.IsFailure || captured.Value.Context.SubjectUserId is not { } subject
            || await requests.GetBySourceAsync(contact.Id, message.Id, WhatsAppAssistantRequestKind.Automatic, ct) is not null) return null;
        var context = captured.Value.Context;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        return (WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contact.Id, message.Id, subject, subject, WhatsAppAssistantRequestKind.Automatic,
            contact.ModeRevision, contact.LinkRevision, JsonSerializer.Serialize(context, JsonSerializerOptions.Web), nowUtc, nowUtc.AddDays(1)), context.Culture);
    }

    // The fixed texts of DOC4, which name the file (a PDF, a photo, a Word) and say what to send instead. Only a Word is empty and only
    // a photo, or a file over the limit before its bytes were known, is too big; anything else (a spreadsheet sent as a document) did not open.
    private static string Text(StructuredAttachmentRejection rejection, StructuredAttachmentFileKind fileKind, string culture) => AssistantTerminalTexts.Get(rejection switch
    {
        StructuredAttachmentRejection.Protected => fileKind == StructuredAttachmentFileKind.Pdf ? "DocumentProtectedPdf" : "DocumentProtected",
        StructuredAttachmentRejection.Legacy => fileKind == StructuredAttachmentFileKind.Word ? "DocumentLegacyWord" : "DocumentLegacy",
        StructuredAttachmentRejection.MacroEnabled => fileKind == StructuredAttachmentFileKind.Word ? "DocumentMacroEnabledWord" : "DocumentMacroEnabled",
        StructuredAttachmentRejection.TooLarge => fileKind switch
        {
            StructuredAttachmentFileKind.Image => "DocumentImageTooLarge",
            StructuredAttachmentFileKind.Unknown => "DocumentTooLarge",
            _ => "DocumentUnreadable",
        },
        StructuredAttachmentRejection.TooManyPages => "DocumentTooManyPages",
        StructuredAttachmentRejection.Empty => fileKind == StructuredAttachmentFileKind.Word ? "DocumentEmpty" : "DocumentUnreadable",
        StructuredAttachmentRejection.Unreadable => "DocumentUnreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "A document that is not available is not answered."),
    }, culture);
}
