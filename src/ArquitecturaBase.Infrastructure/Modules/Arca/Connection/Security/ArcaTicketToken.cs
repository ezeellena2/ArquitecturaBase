using System.Text;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Soap;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;

/// <summary>
/// Lo que dice el token del WSAA sobre a quién representa. El token es un <c>&lt;sso&gt;</c> en base64 y, según el manual
/// del WSAA (6.3), sus <c>relations</c> son «las CUITs autorizadas a actuar como representadas» para el servicio.
/// </summary>
internal static class ArcaTicketToken
{
    /// <summary>Las CUIT representadas; null si el token no se puede leer, porque entonces no dice nada.</summary>
    public static IReadOnlySet<string>? ReadRelations(string token)
    {
        var bytes = new byte[token.Length];
        if (!Convert.TryFromBase64String(token, bytes, out var length)) return null;
        var root = SoapEnvelope.Parse(Encoding.UTF8.GetString(bytes, 0, length))?.Root;
        if (root?.Name.LocalName != "sso") return null;
        return root.Descendants().Where(element => element.Name.LocalName == "relation")
            .Select(relation => (string?)relation.Attribute("key"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }
}
