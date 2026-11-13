using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Stage 2 of the assistant authoring: a spreadsheet that is not read keeps its reason and gets one fixed text with it, as the
// assistant answers: its own automatic reference on the message, accepted like an acknowledgement in the same outbox. Only in the
// person's automatic conversation, in their language and within the 24-hour window. The reference names its own request as the
// proposal, so the reader leaves it out of the conversation: a change or an answer the person was asked for still waits for the next
// message, and its text is no offer. It runs in the caller's transaction and never reaches the model.
internal sealed class WhatsAppSpreadsheetReplyPolicy(WhatsAppAssistantAccessPolicy access, WhatsAppAssistantAutomaticPolicy automatic,
    IWhatsAppAssistantRepository requests, WhatsAppReceivedDocumentReplyPolicy documents, TimeProvider clock)
{
    /// <summary>
    /// VC1-A (rule B8, DOC4): a linked account that can neither load nor build, while some form reads documents, is told so once and the
    /// conversation goes to a person, with the documents' hand-off; in the caller's transaction.
    /// </summary>
    public Task RefuseAsync(WhatsAppContact contact, WhatsAppMessage message, Guid subject, string personCulture, CancellationToken ct) =>
        documents.RefuseSpreadsheetAsync(contact, message, subject, personCulture, ct);

    public async Task RejectAsync(WhatsAppContact contact, WhatsAppMessage message, StructuredAttachmentRejection rejection, CancellationToken ct)
    {
        if (!message.RejectSpreadsheet(ReasonCode(rejection))) return;
        var captured = await access.CaptureAsync(contact, null, ct);
        if (captured.IsFailure || captured.Value.Context.SubjectUserId is not { } subject
            || await requests.GetBySourceAsync(contact.Id, message.Id, WhatsAppAssistantRequestKind.Automatic, ct) is not null) return;
        var context = captured.Value.Context;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var row = WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contact.Id, message.Id, subject, subject, WhatsAppAssistantRequestKind.Automatic,
            contact.ModeRevision, contact.LinkRevision, JsonSerializer.Serialize(context, JsonSerializerOptions.Web), nowUtc, nowUtc.AddDays(1));
        // A fixed text answers no proposal of the model: the reference names its own request.
        if (automatic.AcceptTerminal(contact, row, new WhatsAppAssistantTerminalResponse(row.RequestId, Text(rejection, context.Culture), context.Culture)))
            requests.Add(row);
    }

    /// <summary>The reason kept with the message, as the chat will show it: Excel viejo, con contraseña, con macros, pasado de tope, ilegible o vacía.</summary>
    public static string ReasonCode(StructuredAttachmentRejection rejection) => rejection switch
    {
        StructuredAttachmentRejection.Legacy => "legacy",
        StructuredAttachmentRejection.Protected => "protected",
        StructuredAttachmentRejection.MacroEnabled => "macroEnabled",
        StructuredAttachmentRejection.TooLarge => "tooLarge",
        StructuredAttachmentRejection.Unreadable => "unreadable",
        StructuredAttachmentRejection.Empty => "empty",
        StructuredAttachmentRejection.TooManyRows => "tooManyRows",
        _ => "unavailable",
    };

    private static string Text(StructuredAttachmentRejection rejection, string culture) => AssistantTerminalTexts.Get(rejection switch
    {
        StructuredAttachmentRejection.Legacy => "SpreadsheetLegacy",
        StructuredAttachmentRejection.Protected => "SpreadsheetProtected",
        StructuredAttachmentRejection.MacroEnabled => "SpreadsheetMacroEnabled",
        StructuredAttachmentRejection.TooLarge => "SpreadsheetTooLarge",
        StructuredAttachmentRejection.Unreadable => "SpreadsheetUnreadable",
        StructuredAttachmentRejection.Empty => "SpreadsheetEmpty",
        // VC1-A (decision 11): over the fifty rows to load.
        StructuredAttachmentRejection.TooManyRows => "SpreadsheetTooManyRows",
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "A spreadsheet that is not available is not answered."),
    }, culture);
}
