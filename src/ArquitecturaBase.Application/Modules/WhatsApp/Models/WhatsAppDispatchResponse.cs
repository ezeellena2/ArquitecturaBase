using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppDispatchResponse(Guid Id, Guid ContactId, PhoneNumber To, string Body)
{
    public override string ToString() => nameof(WhatsAppDispatchResponse);
}
