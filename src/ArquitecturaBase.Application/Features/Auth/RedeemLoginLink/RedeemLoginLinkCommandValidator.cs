using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Auth.RedeemLoginLink;

internal sealed class RedeemLoginLinkCommandValidator : AbstractValidator<RedeemLoginLinkCommand>
{
    public RedeemLoginLinkCommandValidator() => RuleFor(command => command.Token).ValidLoginLinkToken();
}
