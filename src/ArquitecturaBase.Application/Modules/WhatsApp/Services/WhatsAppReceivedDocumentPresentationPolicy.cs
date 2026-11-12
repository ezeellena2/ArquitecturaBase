using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Task 8 of the documents: a received document in the operator's chat is a message of its own, with its name, its kind and its pages while
// its descriptor lives (retention takes them with the body). A photo comes without a name (Meta's image has none): the one the message
// keeps for its copy and the inbox («image.jpg») is technical, so the chat gets none and titles it, only when the kind (what its owner
// detected once it was copied, or before, what Meta declared) is a photo: a PDF or a Word that came as a photo keeps that name. The line
// of DOC4 is only the reason of an unlinked number.
internal static class WhatsAppReceivedDocumentPresentationPolicy
{
    public static WhatsAppReceivedDocumentResponse? View(WhatsAppMessageKind kind, WhatsAppReceivedDocumentMessageRow? row)
    {
        if (kind != WhatsAppMessageKind.ReceivedDocument || row?.FileName is not { } name) return null;
        var detected = Kind(row.StorageKey, row.ContentType);
        var nameless = detected is StructuredDocumentKind.Jpeg or StructuredDocumentKind.Png
            && name == WhatsAppMessage.DocumentName(null, WhatsAppReceivedDocumentPreparationPolicy.MediaType(detected.Value));
        return new WhatsAppReceivedDocumentResponse(nameless ? null : name)
        {
            Kind = detected switch
            {
                StructuredDocumentKind.Pdf => "pdf",
                StructuredDocumentKind.Jpeg => "jpeg",
                StructuredDocumentKind.Png => "png",
                StructuredDocumentKind.Docx => "docx",
                _ => null,
            },
            Pages = detected switch
            {
                StructuredDocumentKind.Pdf => row.Pages,
                StructuredDocumentKind.Jpeg or StructuredDocumentKind.Png => 1,
                _ => null,
            },
            ReasonCode = row is { State: WhatsAppDocumentState.Unavailable, FailureCode: WhatsAppReceivedDocumentPreparationPolicy.NotLinkedCode }
                ? WhatsAppReceivedDocumentPreparationPolicy.NotLinkedCode : null,
        };
    }

    private static StructuredDocumentKind? Kind(string? storageKey, string? contentType) =>
        WhatsAppReceivedDocumentPreparationPolicy.TryParseKey(storageKey, out _, out var stored) ? stored
            : contentType?.Split(';')[0].Trim().ToLowerInvariant() switch
            {
                "application/pdf" => StructuredDocumentKind.Pdf,
                "image/jpeg" => StructuredDocumentKind.Jpeg,
                "image/png" => StructuredDocumentKind.Png,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => StructuredDocumentKind.Docx,
                _ => null,
            };
}
