using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.ErrorHandling;

/// <summary>
/// Convierte cualquier excepción en ProblemDetails. Los errores de binding (JSON inválido, fecha sin offset)
/// son un 400 "Request.Invalid"; el resto, un 500 genérico con traceId y el detalle solo en el log.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        if (exception is BadHttpRequestException badRequest)
        {
            problem = ProblemDetailsMapper.Create(
                ErrorType.Validation, ApiErrorCodes.InvalidRequest, ErrorMessages.Get(ApiErrorCodes.InvalidRequest), badRequest.StatusCode);
        }
        else
        {
            LogUnhandledException(logger, exception);
            problem = ProblemDetailsMapper.Create(
                ErrorType.Failure, ApiErrorCodes.Unexpected, ErrorMessages.Get(ApiErrorCodes.Unexpected));
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        // No se pasa la excepción al contexto: así ningún detalle interno llega a la respuesta.
        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while processing the request")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);
}
