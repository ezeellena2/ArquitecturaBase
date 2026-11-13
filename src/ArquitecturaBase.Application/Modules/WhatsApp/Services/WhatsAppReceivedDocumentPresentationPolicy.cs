using System.Text.Json;
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
    /// <summary>
    /// Task 9: the document with «Por qué lo propuso» or «Por qué preguntó»: the doubtful count with the chat, the values read only with
    /// <c>formrecords.read</c>, from the session's sealed content while it lives. The forms are named in the reader's language
    /// (<paramref name="culture"/>) when the chat is read, by the ids kept with what was sent (<paramref name="formNames"/>, the names the
    /// channel's forms have now); one the chat cannot name now keeps the name it was sent with.
    /// </summary>
    public static WhatsAppReceivedDocumentResponse? View(WhatsAppMessageKind kind, WhatsAppReceivedDocumentMessageRow? row,
        WhatsAppAssistantPresentationPolicy presentation, bool canReadRecords, IReadOnlyDictionary<Guid, string> formNames, string culture) =>
        View(kind, row) is { } view && row?.Why is { } why && Why(why, presentation, canReadRecords, formNames, culture) is { } explained
            ? view with { Why = explained } : View(kind, row);

    private static WhatsAppReceivedDocumentWhyResponse? Why(WhatsAppReceivedDocumentWhyRow why, WhatsAppAssistantPresentationPolicy presentation, bool canReadRecords,
        IReadOnlyDictionary<Guid, string> formNames, string culture)
    {
        // A form with a location question opens with its location request, which is what the reference keeps.
        if (why.Kind is WhatsAppOutgoingMessageKind.Flow or WhatsAppOutgoingMessageKind.LocationRequest)
        {
            var name = why.DefinitionId is { } definition && formNames.TryGetValue(definition, out var current) ? current : SentName(why, culture);
            if (name is null) return null;
            var content = presentation.ExplainDocument(why.Assistant, canReadRecords);
            return new("proposed", [name]) { Doubtful = content?.Doubtful, Fields = content?.Fields };
        }
        if (why.Kind is not (WhatsAppOutgoingMessageKind.QuickReplies or WhatsAppOutgoingMessageKind.List) || why.QuickRepliesJson is null) return null;
        var options = JsonSerializer.Deserialize<WhatsAppOutgoingQuickReply[]>(why.QuickRepliesJson, JsonSerializerOptions.Web) ?? [];
        string? Named(string? replyId)
        {
            if (!WhatsAppAssistantDocumentReplyCodec.TryParse(replyId, out _, out _, out var definitionId)) return null;
            if (definitionId is not { } chosen) return WhatsAppAssistantDocumentPolicy.Text("DocumentOther", culture);
            var offered = options.FirstOrDefault(option => option.Id == replyId);
            return formNames.TryGetValue(chosen, out var current) ? current : offered?.Description ?? offered?.Title;
        }
        var forms = options.Where(option => WhatsAppAssistantDocumentReplyCodec.TryParse(option.Id, out _, out _, out var definitionId) && definitionId is not null)
            .Select(option => Named(option.Id)).OfType<string>().ToArray();
        return forms.Length == 0 ? null : new("asked", forms) { Chosen = why.ChosenReplyId is { } tapped ? Named(tapped) ?? why.Chosen : why.Chosen };
    }

    // The names the form had when it was sent: in the reader's language, else in the one it was sent in, else any.
    private static string? SentName(WhatsAppReceivedDocumentWhyRow why, string culture)
    {
        if (why.FormNameJson is null) return null;
        var names = JsonSerializer.Deserialize<Dictionary<string, string>>(why.FormNameJson, JsonSerializerOptions.Web);
        if (names is null) return null;
        return names.GetValueOrDefault(culture) is { Length: > 0 } reader ? reader
            : why.FormCulture is not null && names.GetValueOrDefault(why.FormCulture) is { Length: > 0 } sent ? sent
            : names.Values.FirstOrDefault(name => name.Length > 0);
    }

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
