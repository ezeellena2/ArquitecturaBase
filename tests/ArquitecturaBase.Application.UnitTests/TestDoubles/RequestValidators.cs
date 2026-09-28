using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles;

/// <summary>
/// Arma un <see cref="IRequestValidator"/> real (<see cref="RequestValidator"/>) sobre un contenedor con los
/// validadores dados, cada uno registrado bajo su propio <c>IValidator&lt;T&gt;</c> cerrado. No reimplementa el
/// agrupado de errores ni el camelCase: los resuelve la clase real.
/// </summary>
internal static class RequestValidators
{
    public static IRequestValidator For(params IValidator[] validators)
    {
        var services = new ServiceCollection();

        foreach (var validator in validators)
        {
            var contract = validator.GetType().GetInterfaces()
                .Single(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IValidator<>));
            services.AddSingleton(contract, validator);
        }

        var provider = services.BuildServiceProvider();
        return new RequestValidator(provider);
    }
}
