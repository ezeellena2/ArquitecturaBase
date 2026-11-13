using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Delivery;

/// <summary>A transactional delivery containing encrypted content only.</summary>
public sealed class QueuedDelivery : Entity
{
    private QueuedDelivery() { }

    public string Channel { get; private set; } = string.Empty;
    public string MessageType { get; private set; } = string.Empty;
    public string ProtectedPayload { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ClaimedAtUtc { get; private set; }

    public static QueuedDelivery Create(string channel, string messageType, string protectedPayload, DateTime nowUtc, DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPayload);
        if (nowUtc.Kind != DateTimeKind.Utc || expiresAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc <= nowUtc)
        {
            throw new ArgumentException("A delivery requires a future UTC expiration.");
        }
        return new QueuedDelivery { Channel = channel, MessageType = messageType, ProtectedPayload = protectedPayload, CreatedAtUtc = nowUtc, ExpiresAtUtc = expiresAtUtc };
    }

    public void Claim(DateTime nowUtc)
    {
        if (ClaimedAtUtc is not null || nowUtc >= ExpiresAtUtc || nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("Only an unclaimed, unexpired delivery can be claimed.");
        }
        ClaimedAtUtc = nowUtc;
    }

    /// <summary>
    /// H8: the worker gives it back because the host stopped while it waited for its turn, or for the retry it was going to make
    /// anyway, so another copy can claim it. A claim interrupted in the middle of a send is never released.
    /// </summary>
    public void Release()
    {
        if (ClaimedAtUtc is null) throw new InvalidOperationException("Only a claimed delivery can be given back.");
        ClaimedAtUtc = null;
    }
}
