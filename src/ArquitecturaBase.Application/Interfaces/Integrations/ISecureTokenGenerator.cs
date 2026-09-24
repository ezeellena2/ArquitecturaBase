namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// Tokens de un solo uso que no se pueden adivinar, como el del enlace de ingreso (sección 6.4 del spec del ingreso con
/// WhatsApp): 32 bytes al azar, 256 bits. Con eso el hash no necesita una clave, como sí la necesitan los códigos de 6
/// dígitos, y el token se busca por su hash sin comparar en tiempo constante: medir cuánto tarda una búsqueda no
/// acerca a nadie a un token que no tiene.
/// </summary>
public interface ISecureTokenGenerator
{
    /// <summary>El largo de un token: 32 bytes en base64url sin relleno son 43 caracteres.</summary>
    const int TokenLength = 43;

    /// <summary>
    /// Un token nuevo: 32 bytes de <c>RandomNumberGenerator</c> en base64url sin relleno, es decir, 43 caracteres de
    /// A-Z, a-z, 0-9, "-" y "_". Va en una URL tal cual, sin escaparlo.
    /// </summary>
    string Generate();

    /// <summary>
    /// El SHA-256 del token en hexadecimal y en mayúsculas (64 caracteres), el mismo formato que el hash de los
    /// códigos. Es lo único que se guarda: con la base en la mano no se puede armar el enlace.
    /// </summary>
    string Hash(string token);

    /// <summary>
    /// Si el texto tiene la forma de un token: el largo exacto y solo caracteres de base64url. No dice si el token
    /// existe; eso lo dice la búsqueda por su hash.
    /// </summary>
    static bool HasTokenFormat(string? token) =>
        token is { Length: TokenLength } && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
