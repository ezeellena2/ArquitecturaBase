using System.Text.Json;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// Casos del asistente: the agent that answers the conversations in Asistente mode, with its profile, own cases, «Cualquier otra
/// cosa», limits and allowed actions. It is a row, not a single setting, so the Agents stage adds more rows; today there is only
/// the main one. Saved whole, with a revision and 409, like the rest of the internal configuration (ADR 0018). Until it is saved
/// the assistant does what it did before: it offers forms and answers nothing else.
/// </summary>
public sealed partial class WhatsAppAssistantAgent : Entity, IAuditable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private WhatsAppAssistantAgent() { }

    public bool IsMain { get; private set; }
    public string OriginalCulture { get; private set; } = "es";
    public long Revision { get; private set; }
    public WhatsAppAssistantProfile Profile { get; private set; } = new("", "", "", "", "", new(null, null));
    public IReadOnlyList<WhatsAppAssistantCase> Cases { get; private set; } = [];

    /// <summary>«Cualquier otra cosa»: always there, always last; it cannot be removed, paused or moved. Starts in «No responder».</summary>
    public WhatsAppAssistantAction Fallback { get; private set; } = new(WhatsAppAssistantActionKind.NoReply);

    public WhatsAppAssistantLimits Limits { get; private set; } = WhatsAppAssistantAgentLimits.DefaultLimits;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    /// <summary>The main agent, before its first save; its texts are written in <paramref name="originalCulture"/>.</summary>
    public static WhatsAppAssistantAgent Create(string originalCulture)
    {
        if (originalCulture is not ("es" or "en")) throw new ArgumentException("The agent's original language is es or en.", nameof(originalCulture));
        return new WhatsAppAssistantAgent { IsMain = true, OriginalCulture = originalCulture };
    }

    /// <summary>
    /// Saves the four tabs at once. Texts are trimmed; a case keeps only what its action uses. Saving the same configuration
    /// writes nothing and keeps the revision (the value is false).
    /// </summary>
    public Result<bool> Update(WhatsAppAssistantProfile profile, IReadOnlyList<WhatsAppAssistantCase> cases,
        WhatsAppAssistantAction fallback, WhatsAppAssistantLimits limits)
    {
        var rules = new Rules(OriginalCulture);
        var normalized = rules.Normalize(profile, cases, fallback, limits);
        if (!rules.Valid) return WhatsAppAssistantAgentErrors.Invalid;
        if (Revision > 0 && Same(normalized))
        {
            return false;
        }

        (Profile, Cases, Fallback, Limits) = normalized;
        Revision = checked(Revision + 1);
        return true;
    }

    public override string ToString() => "WhatsAppAssistantAgent { content: [REDACTED] }";

    private bool Same((WhatsAppAssistantProfile Profile, IReadOnlyList<WhatsAppAssistantCase> Cases, WhatsAppAssistantAction Fallback,
        WhatsAppAssistantLimits Limits) next) =>
        Json(Profile) == Json(next.Profile) && Json(Cases) == Json(next.Cases) && Json(Fallback) == Json(next.Fallback) && Json(Limits) == Json(next.Limits);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
}
