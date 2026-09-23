using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Abstractions.Security;

namespace ArquitecturaBase.Infrastructure.Security;

/// <summary>
/// 32 bytes de <see cref="RandomNumberGenerator"/> en base64url, y su SHA-256 en hexadecimal. Sin clave: con 256 bits
/// al azar no hay nada que probar por fuerza bruta, que es lo que la clave de los códigos evita.
/// </summary>
internal sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    private const int TokenBytes = 32;

    public string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    public string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
