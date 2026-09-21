using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users.UpdateProfile;

internal sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(command => command.DisplayName).MaxLength(ValidationRules.DisplayNameMaxLength);

        RuleFor(command => command.Culture)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(UserCultures.IsSupported)
            .WithMessage(_ => ValidationMessages.CultureInvalid);

        RuleFor(command => command.TimeZoneId)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(IsKnownTimeZone)
            .WithMessage(_ => ValidationMessages.TimeZoneInvalid);
    }

    // Identificadores IANA: .NET los resuelve en Windows y en Linux desde .NET 6 (ICU).
    private static bool IsKnownTimeZone(string? timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId!, out _);
}
