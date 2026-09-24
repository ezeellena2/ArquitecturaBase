using System.Text.Json;
using ArquitecturaBase.Domain.Results;
using FluentValidation;
using FluentValidation.Results;

namespace ArquitecturaBase.Application.Common.Validation;

public sealed class ServiceRequestValidator<TRequest>(IEnumerable<IValidator<TRequest>> validators)
{
    public Task<ValidationError?> ValidateAsync(TRequest request, CancellationToken cancellationToken) =>
        ValidateAsync(request, validators, cancellationToken);

    internal static async Task<ValidationError?> ValidateAsync(
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
