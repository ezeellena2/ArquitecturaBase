using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Common.Validation;

/// <summary>
/// Errores que arma un caso de uso y que igual van debajo de un campo del formulario, como los del validador. Los
/// campos van como en el JSON, en camelCase y con puntos ("invitation.consent").
/// </summary>
internal static class FieldErrors
{
    /// <summary>
    /// Un error de negocio con su código, atado a <paramref name="field"/>: la respuesta conserva el código (el front
    /// decide por él) y trae también <c>errors</c>, con el mismo texto traducido que el <c>detail</c>.
    /// </summary>
    public static ValidationError On(Error error, string field)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ValidationError(
            error.Code,
            error.Description,
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [ErrorMessages.Find(error.Code) ?? error.Description] });
    }

    /// <summary>Un error de validación común (<c>Validation.Failed</c>) en un solo campo.</summary>
    public static ValidationError Validation(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
