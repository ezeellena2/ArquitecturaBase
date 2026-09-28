using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// Deja la fila de <see cref="LoginAudit"/> de un intento de ingreso, con el IP y el navegador de la petición. La hora la
/// toma al escribir, así queda después del lock que pone en fila los intentos cuando el ingreso toma uno (el código y el
/// enlace siempre; Google solo al vincular una cuenta). Solo agrega la fila: la guarda la unidad de trabajo del punto de
/// entrada que corre el ingreso.
/// </summary>
internal sealed class LoginAuditRecorder(
    ILoginAuditRepository loginAudits,
    IRequestInfo requestInfo,
    TimeProvider timeProvider)
{
    public void Succeeded(string identifier, Guid userId, LoginMethod method) =>
        loginAudits.Add(LoginAudit.Success(
            identifier, userId, method, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

    /// <summary>Anota el fallo con su código (nunca lo que se ingresó) y devuelve el mismo error, para retornarlo.</summary>
    public Error Failed(string identifier, Guid? userId, LoginMethod method, Error error)
    {
        loginAudits.Add(LoginAudit.Failure(
            identifier, userId, method, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return error;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
