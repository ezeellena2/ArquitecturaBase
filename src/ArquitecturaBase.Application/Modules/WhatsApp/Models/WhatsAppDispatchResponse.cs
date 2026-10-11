using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppDispatchResponse(Guid Id, Guid ContactId, PhoneNumber To, string Body,
    WhatsAppOutgoingMessageKind Kind = WhatsAppOutgoingMessageKind.Text, Guid? FlowSessionId = null)
{
    public override string ToString() => nameof(WhatsAppDispatchResponse);
}
