using System.Diagnostics;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ArquitecturaBase.Api.ErrorHandling;

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
        problem.Extensions.TryAdd(
            ProblemDetailsMapper.TraceIdExtension,
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
