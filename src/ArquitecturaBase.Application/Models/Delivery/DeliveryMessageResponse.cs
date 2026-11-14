namespace ArquitecturaBase.Application.Models.Delivery;

public sealed record DeliveryMessageResponse(Guid Id, string MessageType, string ProtectedPayload)
{
    /// <summary>H3: when the message stops being worth sending, so a worker that waits for its turn knows when to give up.</summary>
    public DateTime? ExpiresAtUtc { get; init; }

    public override string ToString() => nameof(DeliveryMessageResponse);
}
