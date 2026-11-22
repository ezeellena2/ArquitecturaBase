using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;

/// <summary>
/// RC7-B: el aviso de vencimiento del certificado activo, que corre el worker de Infrastructure. Cuando el certificado entra en
/// los días de la conexión, encola un correo para cada persona que administra la conexión y lo marca avisado, en una sola
/// transacción con el lock de la conexión: uno por certificado. Si la cola no toma un correo, no queda ninguno y la corrida
/// siguiente vuelve a probar (<c>ExpiryNoticeNotQueued</c>); sin nadie a quien avisar, el certificado queda sin marcar.
/// </summary>
public interface IArcaCertificateNoticeService
{
    Task<Result> NotifyAsync(CancellationToken cancellationToken);
}
