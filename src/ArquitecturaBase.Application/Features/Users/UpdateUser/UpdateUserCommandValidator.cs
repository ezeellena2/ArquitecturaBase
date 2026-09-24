using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users.UpdateUser;

internal sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(command => command.DisplayName).MaxLength(ValidationRules.DisplayNameMaxLength);

        // A diferencia del alta, la edición manda siempre la lista completa: sin roles no se sabe si es
        // "dejalos como están" o "saquenle todos".
        RuleFor(command => command.Roles).Required();

        // Opcionales, como en el alta: si el número es un celular de un país habilitado lo decide el caso de uso.
        RuleFor(command => command.Email).ValidEmail().When(command => !string.IsNullOrWhiteSpace(command.Email));

        When(command => command.Phone is not null, () =>
        {
            RuleFor(command => command.Phone!.Number).MaxLength(RequestWhatsAppLoginCodeCommandValidator.NumberMaxLength);
            RuleFor(command => command.Phone!.Country).OptionalCountry();
        });
    }
}
