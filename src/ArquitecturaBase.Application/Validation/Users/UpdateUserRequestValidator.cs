using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Users;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    private const int PhoneNumberMaxLength = 32;

    public UpdateUserRequestValidator()
    {
        RuleFor(request => request.DisplayName).MaxLength(ValidationRules.DisplayNameMaxLength);
        RuleFor(request => request.Roles).Required();
        RuleFor(request => request.Email).ValidEmail().When(request => !string.IsNullOrWhiteSpace(request.Email));

        When(request => request.Phone is not null, () =>
        {
            RuleFor(request => request.Phone!.Number).MaxLength(PhoneNumberMaxLength);
            RuleFor(request => request.Phone!.Country).OptionalCountry();
        });
    }
}
