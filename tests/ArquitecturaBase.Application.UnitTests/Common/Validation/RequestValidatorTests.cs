using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Common.Validation;

public sealed class RequestValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

        var validator = RequestValidators.For(required, minimumLength, duplicate);

        var error = Assert.IsType<ValidationError>(
            await validator.ValidateAsync(new Request(new Address(null)), Ct));

        Assert.Equal(1, secondValidatorCalls);
        Assert.Equal(["address.street"], error.Errors.Keys);
        Assert.Equal([requiredMessage, "Too short."], error.Errors["address.street"]);
    }

    [Fact]
    public async Task Valid_request_returns_no_error()
    {
        var validator = RequestValidators.For();

        var error = await validator.ValidateAsync(new Request(new Address("Main Street")), Ct);

        Assert.Null(error);
    }

    /// <summary>Los validadores registrados para otro tipo de pedido no corren: cada uno resuelve solo su IValidator&lt;T&gt;.</summary>
    [Fact]
    public async Task Validators_registered_for_another_request_type_do_not_run()
    {
        var alwaysFailsForOtherRequest = new InlineValidator<OtherRequest>();
        alwaysFailsForOtherRequest.RuleFor(request => request.Value).Must(_ => false).WithMessage("Never valid.");

        var validator = RequestValidators.For(alwaysFailsForOtherRequest);

        var error = await validator.ValidateAsync(new Request(new Address("Main Street")), Ct);

        Assert.Null(error);
        // Control: el mismo validador sí corre para su propio tipo, así que el null de arriba no es un registro vacío.
        Assert.IsType<ValidationError>(await validator.ValidateAsync(new OtherRequest("x"), Ct));
    }

    /// <summary>Cada validador se resuelve recién al llamar a ValidateAsync, así que uno con una dependencia scoped
    /// funciona igual que cualquier otro servicio de Application resuelto en un scope.</summary>
    [Fact]
    public async Task A_validator_with_a_scoped_dependency_resolves_inside_a_scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddScoped<IValidator<Request>, ScopedDependentValidator>();
        services.AddScoped<IRequestValidator, RequestValidator>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IRequestValidator>();

        var error = await validator.ValidateAsync(new Request(new Address(null)), Ct);

        Assert.IsType<ValidationError>(error);
    }

    private sealed record Request(Address Address);

    private sealed record Address(string? Street);

    private sealed record OtherRequest(string? Value);

    private sealed class ScopedDependency;

    private sealed class ScopedDependentValidator : AbstractValidator<Request>
    {
        public ScopedDependentValidator(ScopedDependency dependency)
        {
            ArgumentNullException.ThrowIfNull(dependency);
            RuleFor(request => request.Address.Street).NotEmpty();
        }
    }
}
