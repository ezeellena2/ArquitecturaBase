using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ArquitecturaBase.Api.ErrorHandling;

/// <summary>
/// El 400 de MVC cuando no puede enlazar un pedido: un cuerpo ilegible (JSON roto o vacío, una fecha sin offset) o un
/// parámetro que no se puede convertir (<c>?page=abc</c>).
/// </summary>
/// <remarks>
/// La respuesta no trae <c>errors</c> a propósito. Lo que MVC guarda en el ModelState nombra rutas JSON
/// (<c>$.atUtc</c>), propiedades y tipos .NET del modelo, es decir, su forma interna, y eso no se le cuenta al cliente.
/// Los <c>errors</c> por campo los arman los validadores de Application, que usan los nombres del contrato y
/// mensajes traducidos.
/// </remarks>
internal static class MvcInvalidModelStateResponseFactory
{
    public static IActionResult Create(ActionContext context)
    {
        var problemDetailsFactory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            statusCode: StatusCodes.Status400BadRequest,
            title: ErrorMessages.Title(ErrorType.Validation),
            detail: ErrorMessages.Get(ApiErrorCodes.InvalidRequest));

        problem.Extensions[ProblemDetailsMapper.CodeExtension] = ApiErrorCodes.InvalidRequest;
        ProblemDetailsMapper.AddTraceId(problem, context.HttpContext);

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
