using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Security;

/// <summary>HMAC-SHA256 del código con una clave secreta. La comparación en tiempo constante la hace LoginCode.</summary>
internal sealed class LoginCodeHasher(IOptions<LoginCodeHashOptions> options) : ILoginCodeHasher
{
    // Separa el destino, el propósito y el código. No es ":" porque un correo válido lo puede tener
    // ("a:b@example.com"); un salto de línea no lo tiene ningún destino (Email rechaza los espacios y PhoneNumber es
    // "+" y dígitos) ni el nombre de un propósito. El código va último, así que el mensaje se parte de una sola manera
    // y dos combinaciones distintas nunca firman lo mismo.
    private const char Separator = '\n';

    private readonly byte[] _key = Convert.FromBase64String(options.Value.HashKey);

    public string Hash(LoginCodeDestination destination, LoginCodePurpose purpose, string code)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(code);

        // El destino y el propósito entran en el mensaje: el mismo código para otro destino, o para otro propósito,
        // da otro hash (sección 6.3 del spec del ingreso con WhatsApp).
        var message = Encoding.UTF8.GetBytes(string.Join(Separator, destination.Value, purpose.ToString(), code));

        return Convert.ToHexString(HMACSHA256.HashData(_key, message));
    }
}
