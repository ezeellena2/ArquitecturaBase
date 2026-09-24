using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class SendUserInvitationRequestValidator : AbstractValidator<SendUserInvitationRequest>
{
    public SendUserInvitationRequestValidator()
    {
        RuleFor(request => request.Channel)
            .Cascade(CascadeMode.Stop)
            .Required()
            .IsInEnum().WithMessage(_ => ValidationMessages.InvitationChannelInvalid);
    }
}
