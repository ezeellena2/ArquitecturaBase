using ArquitecturaBase.Application.Modules.Arca.Models.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;

/// <summary>
/// RC7-B: a quién le llega el aviso de vencimiento del certificado: las cuentas activas cuyos roles dan «Administrar conexión»
/// (<c>arca.manage</c>; los administradores lo tienen) y que tienen el correo confirmado.
/// </summary>
public interface IArcaNoticeRecipientReader
{
    Task<IReadOnlyList<ArcaNoticeRecipientRow>> ListManagersAsync(CancellationToken cancellationToken);
}
