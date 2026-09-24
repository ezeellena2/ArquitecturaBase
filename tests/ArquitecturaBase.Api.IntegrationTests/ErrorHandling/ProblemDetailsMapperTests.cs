using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Mvc;

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
    public void A_validation_error_with_the_code_of_a_business_rule_keeps_its_code_its_text_and_its_field()
    {
        using var culture = new CultureScope("es");
        var errors = new Dictionary<string, string[]> { ["invitation.consent"] = ["Confirmá el consentimiento."] };

        var problem = ProblemDetailsMapper.FromError(
            new ValidationError("Users.Invitation.ConsentRequired", "Consent is required.", errors));

        Assert.Equal(400, problem.Status);
        Assert.Equal("Users.Invitation.ConsentRequired", problem.Extensions["code"]);
        Assert.Equal("Confirmá que la persona aceptó recibir mensajes por WhatsApp.", problem.Detail);
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

    [Theory]
    [InlineData(401, "Http.Unauthorized", "No autenticado", "Tenés que iniciar sesión para continuar.")]
    [InlineData(403, "Http.Forbidden", "Acceso denegado", "No tenés permiso para realizar esta acción.")]
    [InlineData(404, "Http.NotFound", "No encontrado", "No encontramos lo que buscás.")]
    [InlineData(405, "Http.MethodNotAllowed", "Operación no permitida", "Esta operación no está permitida en este recurso.")]
    [InlineData(409, "Http.Conflict", "Conflicto", "La solicitud entra en conflicto con el estado actual del recurso.")]
    [InlineData(429, "Http.TooManyRequests", "Demasiadas solicitudes", "Hiciste demasiadas solicitudes. Esperá un momento y volvé a intentar.")]
    [InlineData(503, "General.Unexpected", "Error del servidor", "Ocurrió un error inesperado. Si el problema continúa, informá el código de seguimiento.")]
    [InlineData(418, "Request.Invalid", "Datos inválidos", "La solicitud tiene un formato inválido.")]
    public void Framework_problem_gets_a_code_and_translated_texts(int status, string code, string title, string detail)
    {
        using var culture = new CultureScope("es");
        var problem = new ProblemDetails { Status = status, Title = "Framework default title" };

        ProblemDetailsMapper.CompleteFrameworkProblem(problem);

        Assert.Equal(code, problem.Extensions["code"]);
        Assert.Equal(title, problem.Title);
        Assert.Equal(detail, problem.Detail);
    }

    [Fact]
    public void Problems_that_already_have_a_code_are_left_untouched()
    {
        var problem = ProblemDetailsMapper.FromError(Error.NotFound("Test.Widget.NotFound", "Widget not found."));
        var title = problem.Title;
        var detail = problem.Detail;

        ProblemDetailsMapper.CompleteFrameworkProblem(problem);

        Assert.Equal("Test.Widget.NotFound", problem.Extensions["code"]);
        Assert.Equal(title, problem.Title);
        Assert.Equal(detail, problem.Detail);
    }
}
