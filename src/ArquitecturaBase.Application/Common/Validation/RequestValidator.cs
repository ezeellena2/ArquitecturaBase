using System.Text.Json;
using ArquitecturaBase.Domain.Results;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.Common.Validation;

/// <summary>
/// Implementación única de <see cref="IRequestValidator"/>. Recibe el <see cref="IServiceProvider"/> del scope actual
/// (así lo resuelve la inyección de dependencias de por sí) y busca ahí los <c>IValidator&lt;TRequest&gt;</c> de cada
/// pedido, recién al llamar a <see cref="IRequestValidator.ValidateAsync{TRequest}"/>.
/// </summary>
internal sealed class RequestValidator(IServiceProvider serviceProvider) : IRequestValidator
{
    public Task<ValidationError?> ValidateAsync<TRequest>(TRequest request, CancellationToken cancellationToken) =>
        ValidateAsync(request, serviceProvider.GetServices<IValidator<TRequest>>(), cancellationToken);

    internal static async Task<ValidationError?> ValidateAsync<TRequest>(
        TRequest request,
        IEnumerable<IValidator<TRequest>> validators,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = new List<ValidationFailure>();

        // Los validadores comparten el contexto y pueden depender de servicios scoped.
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return null;
        }

        var errors = failures
            .GroupBy(failure => ToFieldName(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return new ValidationError(errors);
    }

    private static string ToFieldName(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
