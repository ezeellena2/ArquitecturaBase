using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

internal static class TypeNamespaceExtensions
{
    /// <summary>
    /// Si el tipo vive en el namespace dado o en uno de sus sub-namespaces. Compara con el punto, así
    /// <c>ArquitecturaBase.Api.Controllers</c> no incluye a un hipotético <c>ArquitecturaBase.Api.ControllersLegacy</c>, y
    /// con el namespace canónico del tipo (<see cref="ModuleNamespaces.Canonical"/>), así un tipo de un módulo, como
    /// <c>ArquitecturaBase.Api.Modules.Control.Controllers.X</c>, vive donde viviría en el núcleo.
    /// </summary>
    public static bool ResidesIn(this Type type, string @namespace) =>
        type.Namespace is { } typeNamespace
        && ModuleNamespaces.Canonical(typeNamespace) is var canonical
        && (canonical == @namespace || canonical.StartsWith(@namespace + ".", StringComparison.Ordinal));
}
