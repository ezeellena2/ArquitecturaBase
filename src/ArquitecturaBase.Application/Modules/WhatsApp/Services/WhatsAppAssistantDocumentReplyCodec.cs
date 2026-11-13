namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Task 9 of the documents: each option of «¿Qué es este documento?» or «No reconocí este documento» names its operation, its proposal and
// the form chosen (or «Otro»), so only the latest question counts and a choice never needs another interpretation to know what it was.
public static class WhatsAppAssistantDocumentReplyCodec
{
    public const string Prefix = "FORM_DOCUMENT:";
    public const string Other = "other";

    public static string Encode(Guid requestId, Guid proposalId, Guid? definitionId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(proposalId, Guid.Empty);
        if (definitionId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(definitionId));
        return $"{Prefix}{requestId:N}:{proposalId:N}:{(definitionId is { } definition ? definition.ToString("N") : Other)}";
    }

    /// <summary>The operation, the proposal and the form chosen; a null form is «Otro».</summary>
    public static bool TryParse(string? value, out Guid requestId, out Guid proposalId, out Guid? definitionId)
    {
        requestId = Guid.Empty; proposalId = Guid.Empty; definitionId = null;
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal) || value.Length < Prefix.Length + 66) return false;
        var content = value.AsSpan(Prefix.Length);
        if (content[32] != ':' || content[65] != ':' || !Guid.TryParseExact(content[..32], "N", out var request)
            || !Guid.TryParseExact(content.Slice(33, 32), "N", out var proposal) || request == Guid.Empty || proposal == Guid.Empty) return false;
        var choice = content[66..];
        Guid? chosen = null;
        if (!choice.SequenceEqual(Other))
        {
            if (choice.Length != 32 || !Guid.TryParseExact(choice, "N", out var definition) || definition == Guid.Empty) return false;
            chosen = definition;
        }
        requestId = request; proposalId = proposal; definitionId = chosen;
        return true;
    }
}
