using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>Los datos de SMTP son obligatorios solo si los emails salen por SMTP.</summary>
internal sealed class SmtpOptionsValidator(IOptions<EmailOptions> emailOptions) : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        if (emailOptions.Value.Delivery != EmailDelivery.Smtp)
        {
            return ValidateOptionsResult.Skip;
        }

        var results = new List<ValidationResult>();

        return Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(results.Select(result => $"{SmtpOptions.SectionName}: {result.ErrorMessage}"));
    }
}
