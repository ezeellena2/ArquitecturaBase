namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence;
internal static class AiAdvisoryLockKeys
{
    public static string Connection => "ai-connection:installation";
    public static string BusinessInformation => "ai-business-information:installation";
    public static string AssistantBudget => "ai-assistant-budget:installation";
    public static string AssistantActor(Guid actorId) => "ai-assistant-actor:" + actorId.ToString("N");
    public static string AssistantConversation(Guid conversationId) => "ai-assistant-conversation:" + conversationId.ToString("N");
}
