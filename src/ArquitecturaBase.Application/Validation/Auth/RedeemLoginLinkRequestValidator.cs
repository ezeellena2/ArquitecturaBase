using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Auth;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Auth;

internal sealed class RedeemLoginLinkRequestValidator : AbstractValidator<RedeemLoginLinkRequest>
{
    public RedeemLoginLinkRequestValidator() => RuleFor(request => request.Token).ValidLoginLinkToken();
}
