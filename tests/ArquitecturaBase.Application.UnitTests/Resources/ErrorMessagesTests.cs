using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Resources;

public sealed class ErrorMessagesTests
{
    [Theory]
    [InlineData("es", "Revisá los campos marcados.")]
    [InlineData("es-AR", "Revisá los campos marcados.")]
    [InlineData("en", "Check the highlighted fields.")]
    [InlineData("en-US", "Check the highlighted fields.")]
    public void Error_code_is_translated_to_the_current_culture(string culture, string expected)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(expected, ErrorMessages.Find(ValidationError.ErrorCode));
    }

    [Fact]
    public void Unknown_code_is_not_found()
    {
        Assert.Null(ErrorMessages.Find("Test.Unknown.Code"));
        Assert.Equal("Test.Unknown.Code", ErrorMessages.Get("Test.Unknown.Code"));
    }

    [Theory]
    [InlineData("es", ErrorType.NotFound, "No encontrado")]
    [InlineData("en", ErrorType.NotFound, "Not found")]
    [InlineData("es", ErrorType.Validation, "Datos inválidos")]
    [InlineData("en", ErrorType.Failure, "Server error")]
    public void Title_depends_on_the_error_type(string culture, ErrorType type, string expected)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(expected, ErrorMessages.Title(type));
    }
}
