using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.ErrorHandling;

public sealed class ProblemDetailsMapperTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.TooManyRequests, 429)]
    [InlineData(ErrorType.Failure, 500)]
    public void Error_type_maps_to_http_status(ErrorType type, int status)
    {
        Assert.Equal(status, ProblemDetailsMapper.ToStatusCode(type));
    }

    [Fact]
    public void Validation_error_maps_to_400_with_translated_texts_and_field_errors()
    {
        using var culture = new CultureScope("es");
        var errors = new Dictionary<string, string[]> { ["email"] = ["Ingresá un correo válido."] };

        var problem = ProblemDetailsMapper.FromError(new ValidationError(errors));

        Assert.Equal(400, problem.Status);
        Assert.Equal("Datos inválidos", problem.Title);
        Assert.Equal("Revisá los campos marcados.", problem.Detail);
        Assert.Equal("Validation.Failed", problem.Extensions["code"]);
        Assert.Same(errors, problem.Extensions["errors"]);
    }

    [Fact]
    public void Known_code_is_translated_to_english()
    {
        using var culture = new CultureScope("en");

        var problem = ProblemDetailsMapper.FromError(new ValidationError(new Dictionary<string, string[]>()));

        Assert.Equal("Invalid data", problem.Title);
        Assert.Equal("Check the highlighted fields.", problem.Detail);
    }

    [Fact]
    public void Unknown_code_falls_back_to_the_error_description()
    {
        using var culture = new CultureScope("en");

        var problem = ProblemDetailsMapper.FromError(Error.NotFound("Test.Widget.NotFound", "Widget not found."));

        Assert.Equal(404, problem.Status);
        Assert.Equal("Not found", problem.Title);
        Assert.Equal("Widget not found.", problem.Detail);
        Assert.Equal("Test.Widget.NotFound", problem.Extensions["code"]);
    }

    [Fact]
    public void Metadata_is_added_as_extensions()
    {
        var metadata = new Dictionary<string, object?> { ["attemptsLeft"] = 3 };

        var problem = ProblemDetailsMapper.FromError(Error.Validation("Auth.LoginCode.Invalid", "Invalid code.", metadata));

        Assert.Equal(3, problem.Extensions["attemptsLeft"]);
    }

    [Fact]
    public void Metadata_cannot_override_reserved_extensions()
    {
        var metadata = new Dictionary<string, object?>
        {
            ["code"] = "Other.Code",
            ["errors"] = "not a dictionary",
            ["traceId"] = "fake",
            ["attemptsLeft"] = 2,
        };

        var problem = ProblemDetailsMapper.FromError(Error.Validation("Auth.LoginCode.Invalid", "Invalid code.", metadata));

        Assert.Equal("Auth.LoginCode.Invalid", problem.Extensions["code"]);
        Assert.False(problem.Extensions.ContainsKey("errors"));
        Assert.False(problem.Extensions.ContainsKey("traceId"));
        Assert.Equal(2, problem.Extensions["attemptsLeft"]);
    }
}
