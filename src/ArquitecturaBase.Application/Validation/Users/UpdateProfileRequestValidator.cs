using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(request => request.DisplayName).MaxLength(ValidationRules.DisplayNameMaxLength);

        RuleFor(request => request.Culture)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(UserCultures.IsSupported)
            .WithMessage(_ => ValidationMessages.CultureInvalid);

        RuleFor(request => request.TimeZoneId)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(IsKnownTimeZone)
            .WithMessage(_ => ValidationMessages.TimeZoneInvalid);
    }

    // Identificadores IANA: .NET los resuelve en Windows y en Linux desde .NET 6 (ICU).
    private static bool IsKnownTimeZone(string? timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId!, out _);
}
