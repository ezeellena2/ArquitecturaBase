namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public static class ArtificialIntelligencePermissions
{
    public const string Read = "ai.read";
    public const string Manage = "ai.manage";
    public const string BusinessRead = "ai.business.read";
    public const string BusinessManage = "ai.business.manage";
    public const string AssistantUse = "ai.assistant.use";
    public static IReadOnlyCollection<string> All { get; } = [Read, Manage, BusinessRead, BusinessManage, AssistantUse];
    /// <summary>Lo que incluye cada permiso que no sirve sin el «Ver» de su área (como <c>Permissions.Includes</c>).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Includes { get; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            [Manage] = [Read],
            [BusinessManage] = [BusinessRead],
        }.AsReadOnly();
}
