using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Auth;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Auth;

internal sealed class PreviewLoginLinkRequestValidator : AbstractValidator<PreviewLoginLinkRequest>
{
    public PreviewLoginLinkRequestValidator() => RuleFor(request => request.Token).ValidLoginLinkToken();
}
