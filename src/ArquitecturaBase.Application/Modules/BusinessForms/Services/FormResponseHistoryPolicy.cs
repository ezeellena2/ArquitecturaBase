using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The activity a channel leaves before the response, from its origin: the location asked before the form and its answer (W1-A), the
/// form sent, and what the person shared while it was open (W3, task 24): the location and the contact with the questions they
/// completed, named by the labels of the version in its display language, and each time the form went out again, by its sender. No
/// values; a question the version does not have is left out, and so is what would name none.
/// </summary>
internal static class FormResponseHistoryPolicy
{
    public static IReadOnlyList<FormResponseHistoryResponse> Channel(StructuredRecordOrigin origin, FormVersionSchema schema, string culture)
    {
        var sender = origin.SenderUserId is { } senderId ? new FormAuthorResponse(senderId, origin.SenderName) : null;
        var history = new List<FormResponseHistoryResponse>();
        if (origin.LocationRequestedAtUtc is { } requested) history.Add(new("LocationRequested", requested, null));
        if (origin.LocationResolvedAtUtc is { } resolved) history.Add(new(origin.LocationSkipped ? "LocationSkipped" : "LocationReceived", resolved, null));
        history.Add(new("Sent", origin.SentAtUtc, sender));
        var labels = schema.Questions.ToDictionary(question => question.FieldId, question => FormRecordPresentationPolicy.Text(question.Label, culture));
        var shared = new List<(FormResponseHistoryResponse Event, int Order)>();
        if (origin.SharedLocation is { } location && Questions(location, labels) is [var place])
            shared.Add((new("LocationReceived", Microseconds(location.AtUtc), null) { Questions = [place] }, 0));
        if (origin.SharedContact is { } contact && Questions(contact, labels) is { Count: 1 or 2 } completed)
            shared.Add((new("ContactReceived", Microseconds(contact.AtUtc), null) { Questions = completed }, 1));
        shared.AddRange(origin.ResentAtUtc.Select(atUtc => (new FormResponseHistoryResponse("SentAgain", atUtc, sender), 2)));
        history.AddRange(shared.OrderBy(item => item.Event.AtUtc).ThenBy(item => item.Order).Select(item => item.Event));
        return history;
    }

    // What a send took keeps the clock's instant (100 ns) and its resend the database's (1 µs): read at the database's precision, a share
    // and the resend it caused are the same moment, and the share goes first.
    private static DateTime Microseconds(DateTime value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    private static IReadOnlyList<string> Questions(StructuredRecordOriginShare share, IReadOnlyDictionary<Guid, string> labels) =>
        [.. share.FieldIds.Select(fieldId => labels.GetValueOrDefault(fieldId)).OfType<string>().Where(label => label.Length > 0)];
}
