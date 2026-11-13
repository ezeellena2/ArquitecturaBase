using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// W3: a send of the contact that has no reply and has not expired, with the first message of the send (its Flow, or its W1-A location
/// request), whose sender and seal its resend continues, and the sealed projection that says which questions receive what is shared.
/// </summary>
public sealed record WhatsAppFlowOpenSend(WhatsAppFlowSession Session, WhatsAppOutgoingMessage First, Guid BindingOperationId, byte[] ProtectedProjection)
{
    public override string ToString() => "WhatsAppFlowOpenSend { Content = [REDACTED] }";
}

/// <summary>
/// W3: an open send ready for the policy, with its projection read, the mode revision its payload sealed and what it took before
/// (<see cref="Previous"/>), which its next resend keeps.
/// </summary>
public sealed record WhatsAppSharedContentSend(WhatsAppFlowSession Session, WhatsAppOutgoingMessage First, WhatsAppFlowProjection Projection, long ModeRevision)
{
    public IReadOnlyList<WhatsAppFlowSharedValue> Previous { get; init; } = [];
    public override string ToString() => "WhatsAppSharedContentSend { Content = [REDACTED] }";
}

/// <summary>W3: what a send took in this pass, a location or a contact, with what goes in each question.</summary>
public sealed record WhatsAppSharedContentTake(WhatsAppSharedContentSend Send, WhatsAppMessageKind Kind, IReadOnlyList<WhatsAppFlowSharedValue> Values)
{
    public override string ToString() => "WhatsAppSharedContentTake { Content = [REDACTED] }";
}

/// <summary>W3: what was written in a question of a send from what the person shared, with its label in the language of the send.</summary>
public sealed record WhatsAppFlowSharedValue(Guid FieldId, string Label, string Value)
{
    public override string ToString() => "WhatsAppFlowSharedValue { Content = [REDACTED] }";
}

/// <summary>W3: everything a send took, written in its questions; it is kept protected for the send and goes with the messages.</summary>
public sealed record WhatsAppFlowSharedValues(int Format, IReadOnlyList<WhatsAppFlowSharedValue> Values)
{
    public override string ToString() => "WhatsAppFlowSharedValues { Content = [REDACTED] }";
}
