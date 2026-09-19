using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Abstractions.Security;

public interface ILoginCodeHasher
{
    /// <summary>Hash del código atado al email: el mismo código para otro email da otro hash.</summary>
    string Hash(Email email, string code);
}
