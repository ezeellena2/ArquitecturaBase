using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Security;

/// <summary>HMAC-SHA256 del código con una clave secreta. La comparación en tiempo constante la hace LoginCode.</summary>
internal sealed class LoginCodeHasher(IOptions<LoginCodeHashOptions> options) : ILoginCodeHasher
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.HashKey);

    public string Hash(Email email, string code)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(code);

        // El email entra en el mensaje: el mismo código para otro email da otro hash.
        var message = Encoding.UTF8.GetBytes(email.Value + ":" + code);

        return Convert.ToHexString(HMACSHA256.HashData(_key, message));
    }
}
