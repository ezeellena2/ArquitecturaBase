namespace ArquitecturaBase.Application.Interfaces.Integrations.Request;

/// <summary>Datos del cliente de la petición actual, para la auditoría de ingresos.</summary>
public interface IRequestInfo
{
    string? IpAddress { get; }

    string? UserAgent { get; }
}
