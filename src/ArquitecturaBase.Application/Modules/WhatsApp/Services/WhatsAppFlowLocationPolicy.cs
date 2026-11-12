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
        var request = Attached(WhatsAppOutgoingMessage.CreateLocationRequest(actor, session.ContactId, requestId, RequestBody(culture, formName),
            session.Id, nowUtc), session);
        if (location.Required) return [request];
        return [request, Attached(WhatsAppOutgoingMessage.CreateLocationSkip(actor, session.ContactId, Guid.CreateVersion7(),
            FlowTexts.Get("LocationOptional", culture), FlowTexts.Get("LocationSkip", culture), session.Id, SkipRequestedAt(nowUtc)), session)];
    }

    /// <summary>
    /// The messages that open a send by the assistant (asked by the user on 2026-11-11), like a person's: its Flow, or the location
    /// request before it. They are the assistant's, for the send's account, with the link, number and mode revision it sealed.
    /// </summary>
    public static IReadOnlyList<WhatsAppOutgoingMessage> OpenAssistant(WhatsAppFlowProjection projection, WhatsAppFlowSession session,
        Guid requestId, string flowBody, string formName, DateTime nowUtc, long linkRevision, string waId, long modeRevision)
    {
        if (Question(projection) is not { } location)
            return [Attached(WhatsAppOutgoingMessage.CreateAssistantFlow(session.UserId, session.ContactId, requestId, flowBody, session.Id, nowUtc,
                linkRevision, waId, modeRevision), session)];
        session.RequestLocation(location.Required, flowBody);
        var culture = session.Culture;
        var request = Attached(WhatsAppOutgoingMessage.CreateAssistantLocationRequest(session.UserId, session.ContactId, requestId,
            RequestBody(culture, formName), session.Id, nowUtc, linkRevision, waId, modeRevision), session);
        if (location.Required) return [request];
        return [request, Attached(WhatsAppOutgoingMessage.CreateAssistantLocationSkip(session.UserId, session.ContactId, Guid.CreateVersion7(),
            FlowTexts.Get("LocationOptional", culture), FlowTexts.Get("LocationSkip", culture), session.Id, SkipRequestedAt(nowUtc), linkRevision, waId, modeRevision), session)];
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

    /// <summary>
    /// The Flow message of a send whose location was resolved, with the body sealed when the send opened. It continues the request
    /// that opened the send, from the same sender: the assistant's keeps the recipient and revisions it sealed, which the reservation
    /// checks again, so a conversation taken in the meantime does not let it out.
    /// </summary>
    public static WhatsAppOutgoingMessage Flow(WhatsAppFlowSession session, WhatsAppOutgoingMessage request, DateTime nowUtc)
    {
        if (session.LocationState is not (WhatsAppFlowLocationState.Received or WhatsAppFlowLocationState.Skipped) || session.FlowBody is not { } body
            || request.Kind != WhatsAppOutgoingMessageKind.LocationRequest || request.FlowSessionId != session.Id)
            throw new InvalidOperationException("Only a send with its location resolved opens its Flow, after its own request.");
        var flow = request.Origin == WhatsAppMessageOrigin.Assistant
            ? WhatsAppOutgoingMessage.CreateAssistantFlow(request.RecipientUserId!.Value, session.ContactId, Guid.CreateVersion7(), body, session.Id,
                nowUtc, request.RecipientLinkRevision!.Value, request.RecipientWaId!, request.AssistantModeRevision!.Value)
            : WhatsAppOutgoingMessage.CreateFlow(request.OperatorUserId!.Value, session.ContactId, Guid.CreateVersion7(), body, session.Id, nowUtc);
        return Attached(flow, session);
    }

    /// <summary>The answer of the location question as its owner reads a location; WhatsApp gives no accuracy.</summary>
    public static string? Answer(WhatsAppFlowLocationState state, decimal? latitude, decimal? longitude) =>
        state == WhatsAppFlowLocationState.Received && latitude is { } lat && longitude is { } lng
            ? JsonSerializer.Serialize(new { latitude = lat, longitude = lng }) : null;

    // The durable outgoing goes out by RequestedAtUtc and then by Id, a version 7 Guid that is not monotonic within a millisecond:
    // the skip button is requested a millisecond after its request, so it never reaches the person first.
    private static DateTime SkipRequestedAt(DateTime requestedAtUtc) => requestedAtUtc.AddMilliseconds(1);

    private static string RequestBody(string culture, string formName) =>
        string.Format(CultureInfo.GetCultureInfo(culture), FlowTexts.Get("LocationRequest", culture), formName);

    private static WhatsAppOutgoingMessage Attached(WhatsAppOutgoingMessage outgoing, WhatsAppFlowSession session)
    {
        outgoing.AttachFlowSession(session);
        return outgoing;
    }
}
