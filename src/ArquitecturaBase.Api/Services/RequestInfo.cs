using ArquitecturaBase.Application.Interfaces.Integrations;

namespace ArquitecturaBase.Api.Services;

/// <summary>IP y user agent de la petición, para la auditoría de ingresos. LoginAudit recorta el user agent.</summary>
internal sealed class RequestInfo(IHttpContextAccessor httpContextAccessor) : IRequestInfo
{
    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var userAgent = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

            return string.IsNullOrEmpty(userAgent) ? null : userAgent;
        }
    }
}
