namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// The id of «Seguir sin ubicación» (W1-A): it names its send in the lowercase «D» form, the one PostgreSQL writes for a uuid, so a
/// query can compare a tap with its send. Meta brings it back as the reply id of the tap; a button already sent keeps this format.
/// </summary>
public static class WhatsAppFlowLocationReply
{
    public const string Prefix = "FLOW_LOCATION_SKIP:";

    public static string Id(Guid sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        return Prefix + sessionId.ToString("D");
    }

    public static bool TryParse(string? value, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return value is not null && value.Length == Prefix.Length + 36 && value.StartsWith(Prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(value.AsSpan(Prefix.Length), "D", out sessionId) && sessionId != Guid.Empty
            && value == Id(sessionId);
    }
}
