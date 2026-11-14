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

    /// <summary>N27 and H25: when a delivery a copy could not read may be claimed again; before then, nothing claims it.</summary>
    public DateTime? AvailableAtUtc { get; private set; }

    /// <summary>N27 and H25: how many times a copy could not read it.</summary>
    public int ReadFailures { get; private set; }

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

    /// <summary>
    /// N27 and H25: the copy that claimed it could not read it (its key ring does not open it, or its version does not know its
    /// content), so it goes back to the queue until <paramref name="availableAtUtc"/>, for another copy, and counts. At
    /// <paramref name="maxReadFailures"/> it does not, and the caller completes it.
    /// </summary>
    public bool LeaveForAnotherCopy(DateTime availableAtUtc, int maxReadFailures)
    {
        if (ClaimedAtUtc is null) throw new InvalidOperationException("Only a claimed delivery can be left for another copy.");
        if (ReadFailures >= maxReadFailures) return false;
        ClaimedAtUtc = null;
        AvailableAtUtc = availableAtUtc;
        ReadFailures++;
        return true;
    }
}
