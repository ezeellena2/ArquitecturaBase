using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// W1-A: a Flow cannot ask for a location, so a form with a location question asks for it before its Flow. The send opens with the
/// location request and, when the question is optional, «Seguir sin ubicación»; the first location that arrives, or that tap,
/// resolves it and lets its Flow go out, and the reply takes the point from the send. It decides and changes the entities; the
/// caller saves.
/// </summary>
internal static class WhatsAppFlowLocationPolicy
{
    /// <summary>The location question of a sealed projection: it keeps its place in the mapping, outside the Flow.</summary>
    public static WhatsAppFlowProjectionField? Question(WhatsAppFlowProjection projection) =>
        projection.Mapping.FirstOrDefault(field => field.Type == "location");

    /// <summary>The messages that open a send by a person: its Flow, or the location request that goes before it.</summary>
    public static IReadOnlyList<WhatsAppOutgoingMessage> Open(WhatsAppFlowProjection projection, WhatsAppFlowSession session, Guid actor,
        Guid requestId, string flowBody, string formName, DateTime nowUtc)
    {
        if (Question(projection) is not { } location)
            return [Attached(WhatsAppOutgoingMessage.CreateFlow(actor, session.ContactId, requestId, flowBody, session.Id, nowUtc), session)];
        session.RequestLocation(location.Required, flowBody);
        var culture = session.Culture;
        var request = Attached(WhatsAppOutgoingMessage.CreateLocationRequest(actor, session.ContactId, requestId,
            string.Format(CultureInfo.GetCultureInfo(culture), FlowTexts.Get("LocationRequest", culture), formName), session.Id, nowUtc), session);
        if (location.Required) return [request];
        return [request, Attached(WhatsAppOutgoingMessage.CreateLocationSkip(actor, session.ContactId, Guid.CreateVersion7(),
            FlowTexts.Get("LocationOptional", culture), FlowTexts.Get("LocationSkip", culture), session.Id, nowUtc), session)];
    }

    /// <summary>
    /// Each location goes to the oldest send of the contact that waits for one and existed when it was sent; a tap on «Seguir sin
    /// ubicación», to its own send. The answer attaches to the send it resolved; what resolves nothing stays a message of the chat.
    /// </summary>
    public static IReadOnlyList<WhatsAppFlowSession> Resolve(IReadOnlyList<WhatsAppFlowSession> waiting, IReadOnlyList<WhatsAppMessage> answers,
        DateTime nowUtc)
    {
        var resolved = new List<WhatsAppFlowSession>();
        foreach (var answer in answers.Where(answer => answer.FlowSessionId is null).OrderBy(answer => answer.InboundSequence))
        {
            var session = answer.Kind == WhatsAppMessageKind.Location
                ? waiting.Where(session => session.CreatedAtUtc <= answer.OccurredAtUtc && session.AwaitsLocation(nowUtc))
                    .OrderBy(session => session.CreatedAtUtc).ThenBy(session => session.Id).FirstOrDefault()
                : answer.Kind == WhatsAppMessageKind.ButtonReply && WhatsAppFlowLocationReply.TryParse(answer.ReplyId, out var sessionId)
                    ? waiting.FirstOrDefault(session => session.Id == sessionId) : null;
            var applied = session is not null && (answer.Kind == WhatsAppMessageKind.Location
                ? answer.Latitude is { } latitude && answer.Longitude is { } longitude && session.ReceiveLocation(latitude, longitude, answer.WaMessageId, nowUtc)
                : session.SkipLocation(answer.WaMessageId, nowUtc));
            if (!applied) continue;
            answer.AttachFlowSession(session!.Id);
            resolved.Add(session);
        }
        return resolved;
    }

    /// <summary>The Flow message of a send whose location was resolved, with the body sealed when the person sent the form.</summary>
    public static WhatsAppOutgoingMessage Flow(WhatsAppFlowSession session, DateTime nowUtc)
    {
        if (session.LocationState is not (WhatsAppFlowLocationState.Received or WhatsAppFlowLocationState.Skipped)
            || session.OperatorUserId is not { } actor || session.FlowBody is not { } body)
            throw new InvalidOperationException("Only a send by a person with its location resolved opens its Flow.");
        return Attached(WhatsAppOutgoingMessage.CreateFlow(actor, session.ContactId, Guid.CreateVersion7(), body, session.Id, nowUtc), session);
    }

    /// <summary>The answer of the location question as its owner reads a location; WhatsApp gives no accuracy.</summary>
    public static string? Answer(WhatsAppFlowLocationState state, decimal? latitude, decimal? longitude) =>
        state == WhatsAppFlowLocationState.Received && latitude is { } lat && longitude is { } lng
            ? JsonSerializer.Serialize(new { latitude = lat, longitude = lng }) : null;

    private static WhatsAppOutgoingMessage Attached(WhatsAppOutgoingMessage outgoing, WhatsAppFlowSession session)
    {
        outgoing.AttachFlowSession(session);
        return outgoing;
    }
}
