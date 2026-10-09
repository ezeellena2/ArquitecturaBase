namespace ArquitecturaBase.Application.Models.Delivery;

public sealed record DeliveryMessageResponse(Guid Id, string MessageType, string ProtectedPayload)
{
    public override string ToString() => nameof(DeliveryMessageResponse);
}
