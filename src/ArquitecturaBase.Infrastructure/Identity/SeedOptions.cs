using System.ComponentModel.DataAnnotations;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Infrastructure.Identity;

internal sealed class SeedOptions : IValidatableObject
{
    public const string SectionName = "Seed";

    /// <summary>Recibe el rol Admin al crearse la cuenta, o al correr el seed si ya existía. Vacío: nadie.</summary>
    public string? AdminEmail { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(AdminEmail) && Email.Create(AdminEmail).IsFailure)
        {
            yield return new ValidationResult("Seed:AdminEmail is not a valid email address.", [nameof(AdminEmail)]);
        }
    }
}
