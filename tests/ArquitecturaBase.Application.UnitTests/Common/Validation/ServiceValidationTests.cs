using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using FluentValidation;

namespace ArquitecturaBase.Application.UnitTests.Common.Validation;

public sealed class ServiceValidationTests
{
    [Theory]
    [InlineData("es", "Este campo es obligatorio.")]
    [InlineData("en", "This field is required.")]
    public async Task Every_validator_runs_and_messages_use_the_current_language_and_json_field_name(
        string culture,
        string requiredMessage)
    {
        using var cultureScope = new CultureScope(culture);
        var secondValidatorCalls = 0;
        var required = new InlineValidator<Request>();
        required.RuleFor(request => request.Address.Street)
            .NotEmpty()
            .WithMessage(_ => ValidationMessages.Required);

        var minimumLength = new InlineValidator<Request>();
        minimumLength.RuleFor(request => request.Address.Street)
            .Must(value =>
            {
                secondValidatorCalls++;
                return value is { Length: >= 4 };
            })
            .WithMessage("Too short.");

        var duplicate = new InlineValidator<Request>();
        duplicate.RuleFor(request => request.Address.Street)
            .NotEmpty()
            .WithMessage("Too short.");

        var validator = new ServiceRequestValidator<Request>([required, minimumLength, duplicate]);

        var error = Assert.IsType<ValidationError>(
            await validator.ValidateAsync(new Request(new Address(null)), TestContext.Current.CancellationToken));

        Assert.Equal(1, secondValidatorCalls);
        Assert.Equal(["address.street"], error.Errors.Keys);
        Assert.Equal([requiredMessage, "Too short."], error.Errors["address.street"]);
    }

    [Fact]
    public async Task Valid_request_returns_no_error()
    {
        var validator = new ServiceRequestValidator<Request>([]);

        var error = await validator.ValidateAsync(
            new Request(new Address("Main Street")),
            TestContext.Current.CancellationToken);

        Assert.Null(error);
    }

    private sealed record Request(Address Address);

    private sealed record Address(string? Street);
}
