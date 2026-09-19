using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.ErrorHandling;

/// <summary>
/// Convierte errores en ProblemDetails (RFC 9457) con el formato de la sección 6.1 del spec:
/// title y detail traducidos, code, errors en validaciones y traceId (lo agrega AddProblemDetails).
/// </summary>
internal static class ProblemDetailsMapper
{
    public const string CodeExtension = "code";
    public const string ErrorsExtension = "errors";
    public const string TraceIdExtension = "traceId";

    private static readonly HashSet<string> ReservedExtensions = new(StringComparer.OrdinalIgnoreCase) { CodeExtension, ErrorsExtension, TraceIdExtension };

    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static ProblemDetails FromError(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // La descripción traducida se busca por código; si no hay traducción, queda la del error.
        var problem = Create(error.Type, error.Code, ErrorMessages.Find(error.Code) ?? error.Description);

        if (error is ValidationError validationError)
        {
            problem.Extensions[ErrorsExtension] = validationError.Errors;
        }

        if (error.Metadata is not null)
        {
            foreach (var (key, value) in error.Metadata)
            {
                // Las claves reservadas son parte del contrato con el front y nunca se toman de Metadata.
                if (ReservedExtensions.Contains(key))
                {
                    continue;
                }

                problem.Extensions.TryAdd(key, value);
            }
        }

        return problem;
    }

    public static ProblemDetails Create(ErrorType type, string code, string detail, int? statusCode = null) => new()
    {
        Status = statusCode ?? ToStatusCode(type),
        Title = ErrorMessages.Title(type),
        Detail = detail,
        Extensions = { [CodeExtension] = code },
    };
}
