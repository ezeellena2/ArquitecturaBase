using System.Text.Json;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>W3: what a send took of something the person shared, when and which questions it filled, without values.</summary>
public sealed record WhatsAppFlowSharedTake(DateTime AtUtc, IReadOnlyList<Guid> FieldIds);

// W3 (task 23): an open send takes one location and one contact that the person shares on their own, the first of each that it can
// use. It keeps what it took without values (SharedContentJson, for the activity) and what was written in each question, protected for
// the send (ProtectedSharedValues, for its resend), which goes with the retention of the messages.
public sealed partial class WhatsAppFlowSession
{
    private static readonly JsonSerializerOptions SharedOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The location the send took, if any.</summary>
    public WhatsAppFlowSharedTake? SharedLocation => ReadShared().Location;

    /// <summary>The contact the send took, if any.</summary>
    public WhatsAppFlowSharedTake? SharedContact => ReadShared().Contact;

    /// <summary>
    /// An open send for something shared at <paramref name="sharedAtUtc"/>: it already existed and its Flow had gone out (a send that
    /// waited for its W1-A location only once that location resolved it), it has no reply and it has not expired.
    /// </summary>
    public bool IsOpenFor(DateTime sharedAtUtc, DateTime nowUtc) =>
        CaptureState == WhatsAppFlowCaptureState.Pending && nowUtc < ExpiresAtUtc && CreatedAtUtc <= sharedAtUtc
        && (LocationState == WhatsAppFlowLocationState.None
            || LocationState is WhatsAppFlowLocationState.Received or WhatsAppFlowLocationState.Skipped && LocationResolvedAtUtc <= sharedAtUtc);

    /// <summary>
    /// The send takes a shared location or contact into these questions, once of each kind and only while it is open for it: what
    /// arrives later goes to another send or stays in the chat.
    /// </summary>
    public bool TakeShared(WhatsAppMessageKind kind, IReadOnlyCollection<Guid> fieldIds, DateTime sharedAtUtc, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(fieldIds);
        if (kind is not (WhatsAppMessageKind.Location or WhatsAppMessageKind.Contact))
            throw new ArgumentOutOfRangeException(nameof(kind), "A send takes only a shared location or contact.");
        if (fieldIds.Count is 0 or > 2 || fieldIds.Contains(Guid.Empty) || fieldIds.Distinct().Count() != fieldIds.Count || nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("What a send takes fills one or two distinct questions, at a UTC time.", nameof(fieldIds));
        var shared = ReadShared();
        if (!IsOpenFor(sharedAtUtc, nowUtc) || (kind == WhatsAppMessageKind.Location ? shared.Location : shared.Contact) is not null) return false;
        var take = new WhatsAppFlowSharedTake(nowUtc, fieldIds.ToArray());
        SharedContentJson = JsonSerializer.Serialize(kind == WhatsAppMessageKind.Location ? shared with { Location = take } : shared with { Contact = take },
            SharedOptions);
        return true;
    }

    /// <summary>What was written in the questions of everything the send took, protected; it replaces what it kept before.</summary>
    public void SealSharedValues(byte[] protectedValues)
    {
        ArgumentNullException.ThrowIfNull(protectedValues);
        if (SharedContentJson is null || protectedValues.Length == 0)
            throw new InvalidOperationException("Only a send that took something shared keeps what was written, never empty.");
        if (ProtectedSharedValues is { } previous) Array.Clear(previous);
        ProtectedSharedValues = protectedValues.ToArray();
    }

    /// <summary>The retention of the messages clears what was written; what the send took stays, without values.</summary>
    public bool ClearSharedValues()
    {
        if (ProtectedSharedValues is not { } values) return false;
        Array.Clear(values);
        ProtectedSharedValues = null;
        return true;
    }

    private SharedContent ReadShared() => SharedContentJson is null ? new(null, null)
        : JsonSerializer.Deserialize<SharedContent>(SharedContentJson, SharedOptions) ?? throw new InvalidOperationException("What the send took is unreadable.");

    private sealed record SharedContent(WhatsAppFlowSharedTake? Location, WhatsAppFlowSharedTake? Contact);
}
