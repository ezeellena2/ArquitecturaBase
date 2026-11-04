using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;

public interface IArcaConnectionTestService
{
    /// <summary>
    /// Prueba los servicios elegidos con la prueba de cada familia, fuera de toda transacción, y después guarda el
    /// resultado. Prueba el certificado de una renovación si ya está cargado; si sale en verde, reemplaza al activo.
    /// </summary>
    Task<Result<ArcaTestResponse>> TestAsync(CancellationToken cancellationToken);

    /// <summary>Vuelve a calcular el estado con la configuración de las familias elegidas; lo llama una familia al guardar lo suyo.</summary>
    Task<Result<ArcaConnectionResponse>> ReevaluateAsync(CancellationToken cancellationToken);
}
