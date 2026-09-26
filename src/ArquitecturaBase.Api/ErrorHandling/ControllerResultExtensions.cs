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

    /// <summary>
    /// Un 202 sin cuerpo y sin <c>Location</c>: lo aceptado se procesa aparte y no queda un recurso para consultar.
    /// </summary>
    public static IActionResult ToAcceptedResult(this Result result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess ? new StatusCodeResult(StatusCodes.Status202Accepted) : ToProblem(result.Error, controller);
    }

    /// <summary>
    /// Un 202 con el valor en el cuerpo si lo hay (por ejemplo, cuánto esperar para pedir otro código) y sin
    /// <c>Location</c>.
    /// </summary>
    public static IActionResult ToAcceptedResult<TValue>(this Result<TValue> result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? result.Value is null
                ? new StatusCodeResult(StatusCodes.Status202Accepted)
                : new AcceptedResult(location: null, value: result.Value)
            : ToProblem(result.Error, controller);
    }

    /// <summary>
    /// Un 201 con el valor en el cuerpo y el <c>Location</c> armado con la acción que devuelve el recurso nuevo. La
    /// acción tiene que existir en el mismo controller: si la ruta no se puede armar, MVC lanza una excepción en lugar de
    /// responder un 201 sin <c>Location</c>.
    /// </summary>
    public static IActionResult ToCreatedResult<TValue>(
        this Result<TValue> result,
        ControllerBase controller,
        string actionName,
        Func<TValue, object> routeValues)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        ArgumentNullException.ThrowIfNull(routeValues);

        return result.IsSuccess
            ? controller.CreatedAtAction(actionName, routeValues(result.Value), result.Value)
            : ToProblem(result.Error, controller);
    }

    private static ObjectResult ToProblem(Error error, ControllerBase controller)
    {
        var problem = ProblemDetailsMapper.FromError(error, controller.ProblemDetailsFactory, controller.HttpContext);

        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
