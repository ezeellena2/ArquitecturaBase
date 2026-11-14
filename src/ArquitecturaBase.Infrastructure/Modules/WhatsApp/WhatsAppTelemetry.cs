using System.Diagnostics;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// H19: the module's traces (its <c>AddSource</c> is in the module's registration). A message and a send keep the W3C context
/// where they were written: the webhook's request, or the round that answered. Each worker continues it: the inbound round from
/// the last message of its chat, and the outgoing from its send, with the call to Meta as a child. No tag carries a number, a
/// body or an id.
/// </summary>
internal static class WhatsAppTelemetry
{
    public const string SourceName = "ArquitecturaBase.WhatsApp";

    private static readonly ActivitySource Source = new(SourceName);

    public static Activity? StartInbound(string? traceParent) => Start("whatsapp.inbound", traceParent);

    public static Activity? StartOutgoing(string? traceParent, WhatsAppMessageOrigin origin, WhatsAppOutgoingMessageKind kind)
    {
        var activity = Start("whatsapp.outgoing", traceParent);
        activity?.SetTag("whatsapp.origin", origin.ToString());
        activity?.SetTag("whatsapp.kind", kind.ToString());
        return activity;
    }

    /// <summary>The context to keep in a row: only a W3C one fits its column.</summary>
    public static string? Current() => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

    private static Activity? Start(string name, string? traceParent) => Source.StartActivity(name, ActivityKind.Consumer,
        ActivityContext.TryParse(traceParent, null, isRemote: true, out var parent) ? parent : default);
}
