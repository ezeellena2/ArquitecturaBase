using System.Text.RegularExpressions;

namespace ArquitecturaBase.Application.UnitTests.Support;

/// <summary>
/// Copia chica de <c>ModuleNamespaces.Canonical</c> de ArchitectureTests, que este proyecto no ve: el namespace de un
/// módulo opcional (<c>ArquitecturaBase.&lt;Capa&gt;.Modules.&lt;M&gt;.X</c>) es la misma capa y la misma carpeta que
/// <c>ArquitecturaBase.&lt;Capa&gt;.X</c>. Así las reglas de DI siguen también las dependencias y los contratos de un
/// módulo.
/// </summary>
internal static partial class ModuleNamespaces
{
    /// <summary>
    /// El nombre sin el segmento del módulo: <c>ArquitecturaBase.Application.Modules.Control.Services</c> →
    /// <c>ArquitecturaBase.Application.Services</c>. Lo que no es de un módulo queda igual.
    /// </summary>
    public static string Canonical(string name) => ModuleSegment().Replace(name, "ArquitecturaBase.${layer}", 1);

    [GeneratedRegex(@"^ArquitecturaBase\.(?<layer>Domain|Application|Infrastructure|Api)\.Modules\.[A-Z][A-Za-z0-9]*(?=\.|$)")]
    private static partial Regex ModuleSegment();
}
