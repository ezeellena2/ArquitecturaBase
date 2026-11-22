using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public sealed class AiConnection : Entity, IAuditable
{
    /// <summary>RC1-A: how many days the Home assistant keeps a conversation, with its audios and documents, until shortened.</summary>
    public const int InitialConversationRetentionDays = 30;

    private AiConnection() { }
    public int Scope { get; private set; } = 1;
    public AiProvider Provider { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public string? ProtectedApiKey { get; private set; }
    public bool Enabled { get; private set; }
    public long Revision { get; private set; }
    public int ConversationRetentionDays { get; private set; } = InitialConversationRetentionDays;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static AiConnection Create() => new();

    /// <summary>
    /// The retention goes from one day to <paramref name="conversationRetentionCeilingDays"/>, the installation's ceiling
    /// (ArtificialIntelligence:Assistant:Limits:RetentionDays), which only Application knows.
    /// </summary>
    public Result Update(AiProvider provider, string model, string? protectedApiKey, bool enabled,
        int conversationRetentionDays = InitialConversationRetentionDays, int conversationRetentionCeilingDays = InitialConversationRetentionDays)
    {
        if (!Enum.IsDefined(provider) || string.IsNullOrWhiteSpace(model) || model.Trim().Length > 150)
            return AiConnectionErrors.Invalid;
        if (conversationRetentionDays < 1 || conversationRetentionDays > conversationRetentionCeilingDays)
            return AiConnectionErrors.RetentionInvalid;
        if (enabled && string.IsNullOrEmpty(protectedApiKey)) return AiConnectionErrors.CredentialRequired;
        Provider = provider;
        Model = model.Trim();
        ProtectedApiKey = protectedApiKey;
        Enabled = enabled;
        ConversationRetentionDays = conversationRetentionDays;
        Revision = checked(Revision + 1);
        return Result.Success();
    }

    public override string ToString() => "AiConnection { credential: [REDACTED] }";
}
