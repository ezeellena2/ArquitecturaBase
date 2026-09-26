namespace ArquitecturaBase.ArchitectureTests;

internal static class TypeNamespaceExtensions
{
    /// <summary>
    /// Si el tipo vive en el namespace dado o en uno de sus sub-namespaces. Compara con el punto, así
    /// <c>ArquitecturaBase.Api.Controllers</c> no incluye a un hipotético <c>ArquitecturaBase.Api.ControllersLegacy</c>.
    /// </summary>
    public static bool ResidesIn(this Type type, string @namespace) =>
        type.Namespace is { } typeNamespace
        && (typeNamespace == @namespace || typeNamespace.StartsWith(@namespace + ".", StringComparison.Ordinal));
}
