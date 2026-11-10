namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaPermissions
{
    public const string Read = "arca.read";
    public const string Manage = "arca.manage";
    /// <summary>Decidir sobre una emisión en revisión y anular una respuesta con factura: efectos con valor legal.</summary>
    public const string Issue = "arca.issue";
    public static IReadOnlyCollection<string> All { get; } = [Read, Manage, Issue];
}
