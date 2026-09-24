using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    private const int PhoneNumberMaxLength = 32;

    public CreateUserRequestValidator()
    {
        RuleFor(request => request.Email).ValidEmail().When(request => !string.IsNullOrWhiteSpace(request.Email));
        RuleFor(request => request.DisplayName).MaxLength(ValidationRules.DisplayNameMaxLength);

        When(request => request.Phone is not null, () =>
        {
            RuleFor(request => request.Phone!.Number).MaxLength(PhoneNumberMaxLength);
            RuleFor(request => request.Phone!.Country).OptionalCountry();
        });

        When(request => request.Invitation is not null, () =>
            RuleFor(request => request.Invitation!.Channel)
                .Cascade(CascadeMode.Stop)
                .Required()
                .IsInEnum().WithMessage(_ => ValidationMessages.InvitationChannelInvalid));
    }
}
