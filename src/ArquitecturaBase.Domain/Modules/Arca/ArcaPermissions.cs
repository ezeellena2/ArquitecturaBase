namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaPermissions
{
    public const string Read = "arca.read";
    public const string Manage = "arca.manage";
    /// <summary>Decidir sobre una emisión en revisión y anular una respuesta con factura: efectos con valor legal.</summary>
    public const string Issue = "arca.issue";
    public static IReadOnlyCollection<string> All { get; } = [Read, Manage, Issue];
    /// <summary>Lo que incluye cada permiso que no sirve sin el «Ver» de su área (como <c>Permissions.Includes</c>).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Includes { get; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal) { [Manage] = [Read] }.AsReadOnly();
}
