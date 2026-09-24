namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// La dirección pública de la web, la que ve el navegador: <c>Authentication:Issuer</c>, que en desarrollo es la de
/// Vite (sección 14 del spec del ingreso con WhatsApp). La usan las direcciones que arma la Api sin un pedido del
/// navegador de por medio, como el enlace que manda el bot: no hay un Host del que deducirla.
/// </summary>
public interface IPublicOrigin
{
    /// <summary>La dirección, siempre terminada en "/", o null si <c>Authentication:Issuer</c> no está configurado.</summary>
    Uri? Value { get; }
}
