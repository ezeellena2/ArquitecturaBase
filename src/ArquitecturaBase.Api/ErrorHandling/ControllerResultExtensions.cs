using System.Diagnostics;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.ErrorHandling;

public static class ControllerResultExtensions
{
    public static IActionResult ToActionResult(this Result result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess ? new NoContentResult() : ToProblem(result.Error, controller);
    }

    public static IActionResult ToActionResult<TValue>(this Result<TValue> result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? result.Value is null
                ? new StatusCodeResult(StatusCodes.Status200OK)
                : new JsonResult(result.Value) { StatusCode = StatusCodes.Status200OK }
            : ToProblem(result.Error, controller);
    }

    private static ObjectResult ToProblem(Error error, ControllerBase controller)
    {
        var problem = ProblemDetailsMapper.FromError(error);
        problem.Extensions.TryAdd(
            ProblemDetailsMapper.TraceIdExtension,
            Activity.Current?.Id ?? controller.HttpContext.TraceIdentifier);

        var response = controller.Problem(
            detail: problem.Detail,
            statusCode: problem.Status,
            title: problem.Title,
            type: problem.Type);

        var responseProblem = (ProblemDetails)response.Value!;
        foreach (var (key, value) in problem.Extensions)
        {
            responseProblem.Extensions[key] = value;
        }

        return response;
    }
}
