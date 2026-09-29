using System.Text.RegularExpressions;

namespace ArquitecturaBase.ArchitectureTests.Support;

/// <summary>
/// Los nombres de un módulo opcional (ADR 0007): vive en una carpeta <c>Modules/&lt;M&gt;</c> de los mismos proyectos, y
/// <c>ArquitecturaBase.&lt;Capa&gt;.Modules.&lt;M&gt;.X</c> es la misma capa y la misma carpeta que
/// <c>ArquitecturaBase.&lt;Capa&gt;.X</c>. Las reglas de arquitectura comparan el nombre canónico, sin el segmento del
/// módulo, y así ven también al módulo. Un módulo se llama con mayúscula inicial y sin puntos.
/// </summary>
internal static partial class ModuleNamespaces
{
    /// <summary>
    /// El nombre sin el segmento del módulo: <c>ArquitecturaBase.Application.Modules.Control.Services.X</c> →
    /// <c>ArquitecturaBase.Application.Services.X</c>. Sirve para un namespace o para el nombre completo de un tipo; lo
    /// que no es de un módulo queda igual.
    /// </summary>
    public static string Canonical(string name) => ModuleSegment().Replace(name, "ArquitecturaBase.${layer}", 1);

    /// <summary>El módulo de un namespace o de un nombre completo (<c>Control</c>), o null si es del núcleo.</summary>
    public static string? ModuleOf(string name) =>
        ModuleSegment().Match(name) is { Success: true } match ? match.Groups["module"].Value : null;

    /// <summary>
    /// Una expresión regular para <c>ResideInNamespaceMatching</c> de NetArchTest que acepta el namespace canónico, sus
    /// sub-namespaces y los mismos adentro de cualquier módulo:
    /// <c>^ArquitecturaBase\.Api(\.Modules\.[A-Z][A-Za-z0-9]*)?\.Controllers(\.|$)</c>.
    /// </summary>
    public static string Matching(string canonicalNamespace)
    {
        var match = LayerAndRest().Match(canonicalNamespace);

        if (!match.Success || ModuleOf(canonicalNamespace) is not null)
        {
            throw new ArgumentException($"'{canonicalNamespace}' is not a canonical namespace of a layer.", nameof(canonicalNamespace));
        }

        return @"^ArquitecturaBase\." + match.Groups["layer"].Value + @"(\.Modules\.[A-Z][A-Za-z0-9]*)?"
            + Regex.Escape(match.Groups["rest"].Value) + @"(\.|$)";
    }

    [GeneratedRegex(@"^ArquitecturaBase\.(?<layer>Domain|Application|Infrastructure|Api)\.Modules\.(?<module>[A-Z][A-Za-z0-9]*)(?=\.|$)")]
    private static partial Regex ModuleSegment();

    [GeneratedRegex(@"^ArquitecturaBase\.(?<layer>Domain|Application|Infrastructure|Api)(?<rest>\..+)?$")]
    private static partial Regex LayerAndRest();
}
