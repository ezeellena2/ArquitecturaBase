using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.ErrorHandling;

public static class ResultExtensions
{
    /// <summary>204 si salió bien; si no, ProblemDetails.</summary>
    public static IResult ToHttpResult(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }

    /// <summary>200 con el valor si salió bien; si no, ProblemDetails.</summary>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    public static IResult ToProblem(this Error error) =>
        TypedResults.Problem(ProblemDetailsMapper.FromError(error));
}
