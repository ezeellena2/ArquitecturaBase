using ArquitecturaBase.Application.Common.Validation;
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
    }
}
