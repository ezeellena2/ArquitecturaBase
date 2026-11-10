namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// IA3-A: each button of an authoring summary names its operation, its proposal and what it does, so only the latest counts. An
// import's summary (IA, Stage 2) reviews instead of creating.
public static class WhatsAppAssistantAuthoringReplyCodec
{
    public const string Prefix = "FORM_AUTHORING:";
    public const string Create = "create";
    public const string Change = "change";
    public const string Review = "review";

    public static string Encode(Guid requestId, Guid proposalId, string action)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(proposalId, Guid.Empty);
        if (action is not (Create or Change or Review)) throw new ArgumentOutOfRangeException(nameof(action));
        return $"{Prefix}{requestId:N}:{proposalId:N}:{action}";
    }

    public static bool TryParse(string? value, out Guid requestId, out Guid proposalId, out string action)
    {
        requestId = Guid.Empty; proposalId = Guid.Empty; action = "";
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal) || value.Length < Prefix.Length + 66) return false;
        var content = value.AsSpan(Prefix.Length);
        var kind = content[66..].ToString();
        if (content[32] != ':' || content[65] != ':' || kind is not (Create or Change or Review) || !Guid.TryParseExact(content[..32], "N", out var request)
            || !Guid.TryParseExact(content.Slice(33, 32), "N", out var proposal) || request == Guid.Empty || proposal == Guid.Empty) return false;
        requestId = request; proposalId = proposal; action = kind;
        return true;
    }
}
