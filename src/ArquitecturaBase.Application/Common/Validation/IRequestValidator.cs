using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Common.Validation;

/// <summary>
/// El único validador que se inyecta en un servicio de Application. Resuelve los <c>IValidator&lt;TRequest&gt;</c>
/// registrados en el scope actual y agrupa sus errores.
/// </summary>
public interface IRequestValidator
{
    Task<ValidationError?> ValidateAsync<TRequest>(TRequest request, CancellationToken cancellationToken);
}
