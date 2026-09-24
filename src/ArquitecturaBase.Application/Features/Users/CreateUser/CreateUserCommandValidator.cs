using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users.CreateUser;

/// <summary>
/// La forma del alta. Que haya un correo o un número, si el número es un celular de un país habilitado y las reglas de
/// la invitación lo decide el caso de uso, que responde con sus propios códigos.
/// </summary>
internal sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        // Opcional: vacío es lo mismo que no mandarlo.
        RuleFor(command => command.Email).ValidEmail().When(command => !string.IsNullOrWhiteSpace(command.Email));
        RuleFor(command => command.DisplayName).MaxLength(ValidationRules.DisplayNameMaxLength);

        When(command => command.Phone is not null, () =>
        {
            RuleFor(command => command.Phone!.Number).MaxLength(RequestWhatsAppLoginCodeCommandValidator.NumberMaxLength);
            RuleFor(command => command.Phone!.Country).OptionalCountry();
        });

        When(command => command.Invitation is not null, () =>
            RuleFor(command => command.Invitation!.Channel)
                .Cascade(CascadeMode.Stop)
                .Required()
                .IsInEnum().WithMessage(_ => ValidationMessages.InvitationChannelInvalid));
    }
}
