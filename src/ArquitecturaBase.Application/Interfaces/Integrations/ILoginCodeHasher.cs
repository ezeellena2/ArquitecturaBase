using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Application.Interfaces.Integrations;

public interface ILoginCodeHasher
{
    /// <summary>
    /// Hash del código atado al destino y al propósito: el mismo código para otro destino, o para otro propósito, da
    /// otro hash (sección 6.3 del spec del ingreso con WhatsApp).
    /// </summary>
    string Hash(LoginCodeDestination destination, LoginCodePurpose purpose, string code);
}
