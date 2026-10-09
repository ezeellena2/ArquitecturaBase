namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public static class ArtificialIntelligencePermissions
{
    public const string Read = "ai.read";
    public const string Manage = "ai.manage";
    public const string BusinessRead = "ai.business.read";
    public const string BusinessManage = "ai.business.manage";
    public const string AssistantUse = "ai.assistant.use";
    public static IReadOnlyCollection<string> All { get; } = [Read, Manage, BusinessRead, BusinessManage, AssistantUse];
}
