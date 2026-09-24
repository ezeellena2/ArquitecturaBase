using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users.SendInvitation;

/// <summary>Solo el canal; las reglas de la invitación, contra lo que tiene la cuenta, las decide el caso de uso.</summary>
internal sealed class SendInvitationCommandValidator : AbstractValidator<SendInvitationCommand>
{
    public SendInvitationCommandValidator()
    {
        RuleFor(command => command.Channel)
            .Cascade(CascadeMode.Stop)
            .Required()
            .IsInEnum().WithMessage(_ => ValidationMessages.InvitationChannelInvalid);
    }
}
