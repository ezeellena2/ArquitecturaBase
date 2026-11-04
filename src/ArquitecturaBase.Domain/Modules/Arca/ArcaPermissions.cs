namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaPermissions
{
    public const string Read = "arca.read";
    public const string Manage = "arca.manage";
    public static IReadOnlyCollection<string> All { get; } = [Read, Manage];
}
