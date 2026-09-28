using System.Diagnostics;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ArquitecturaBase.Api.ErrorHandling;

/// <summary>
/// Convierte errores en ProblemDetails (RFC 9457) con el formato de la sección 6.1 del spec:
/// title y detail traducidos, code, errors en validaciones y traceId (<see cref="AddTraceId"/>).
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

    /// <summary>
    /// Convierte el error de un <see cref="Result"/> que falla en lo que responde un controller: el ProblemDetails lo arma
    /// la fábrica de MVC, que es la que le pone el <c>type</c> según el status y le aplica <c>CustomizeProblemDetails</c>,
    /// como a cualquier otro error del framework, y lleva el code y el detail del error, sus errores por campo, su
    /// metadata (sin pisar las claves reservadas) y el traceId.
    /// </summary>
    public static ProblemDetails FromError(Error error, ProblemDetailsFactory factory, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(httpContext);

        var problem = factory.CreateProblemDetails(
            httpContext,
            statusCode: ToStatusCode(error.Type),
            title: ErrorMessages.Title(error.Type),
            detail: Describe(error));

        // CustomizeProblemDetails ya le puso el code genérico de su status: el del error lo reemplaza.
        problem.Extensions[CodeExtension] = error.Code;
        AddErrorExtensions(problem, error);
        AddTraceId(problem, httpContext);

        return problem;
    }

    /// <summary>
    /// Le pone el traceId a un ProblemDetails que todavía no lo tiene: el id de la actividad en curso, que es el que se
    /// busca en el dashboard de Aspire, o el identificador del request si no hay una. Es el único lugar que lo calcula.
    /// </summary>
    public static void AddTraceId(ProblemDetails problem, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(httpContext);

        problem.Extensions.TryAdd(TraceIdExtension, Activity.Current?.Id ?? httpContext.TraceIdentifier);
    }

    /// <summary>
    /// Completa los ProblemDetails que arma el propio ASP.NET sin pasar por
    /// <see cref="FromError(Error, ProblemDetailsFactory, HttpContext)"/> (ruta inexistente, método incorrecto, 401/403 de
    /// la autorización): les pone code y title traducido según el status, y un detail traducido si no traían uno. Los que ya tienen code no se tocan: los nuestros, y también el 429 del rate
    /// limiter, que arma su propio ProblemDetails con retryAfter en <see cref="RateLimiting.RateLimitingExtensions"/>.
    /// </summary>
    public static void CompleteFrameworkProblem(ProblemDetails problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        if (problem.Extensions.ContainsKey(CodeExtension))
        {
            return;
        }

        var (code, title) = DescribeStatusCode(problem.Status ?? StatusCodes.Status500InternalServerError);

        problem.Title = title;
        problem.Detail ??= ErrorMessages.Get(code);
        problem.Extensions[CodeExtension] = code;
    }

    public static ProblemDetails Create(ErrorType type, string code, string detail, int? statusCode = null) => new()
    {
        Status = statusCode ?? ToStatusCode(type),
        Title = ErrorMessages.Title(type),
        Detail = detail,
        Extensions = { [CodeExtension] = code },
    };

    // La descripción traducida se busca por código; si no hay traducción, queda la del error.
    private static string Describe(Error error) => ErrorMessages.Find(error.Code) ?? error.Description;

    private static void AddErrorExtensions(ProblemDetails problem, Error error)
    {
        if (error is ValidationError validationError)
        {
            problem.Extensions[ErrorsExtension] = validationError.Errors;
        }

        if (error.Metadata is null)
        {
            return;
        }

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

    private static (string Code, string Title) DescribeStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized => (ApiErrorCodes.Unauthorized, ErrorMessages.Title(ErrorType.Unauthorized)),
        StatusCodes.Status403Forbidden => (ApiErrorCodes.Forbidden, ErrorMessages.Title(ErrorType.Forbidden)),
        StatusCodes.Status404NotFound => (ApiErrorCodes.NotFound, ErrorMessages.Title(ErrorType.NotFound)),
        StatusCodes.Status405MethodNotAllowed => (ApiErrorCodes.MethodNotAllowed, ErrorMessages.Get("Title.MethodNotAllowed")),
        StatusCodes.Status409Conflict => (ApiErrorCodes.Conflict, ErrorMessages.Title(ErrorType.Conflict)),
        StatusCodes.Status429TooManyRequests => (ApiErrorCodes.TooManyRequests, ErrorMessages.Title(ErrorType.TooManyRequests)),
        >= StatusCodes.Status500InternalServerError => (ApiErrorCodes.Unexpected, ErrorMessages.Title(ErrorType.Failure)),
        _ => (ApiErrorCodes.InvalidRequest, ErrorMessages.Title(ErrorType.Validation)),
    };
}
