using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Auth.PreviewLoginLink;

internal sealed class PreviewLoginLinkQueryValidator : AbstractValidator<PreviewLoginLinkQuery>
{
    public PreviewLoginLinkQueryValidator() => RuleFor(query => query.Token).ValidLoginLinkToken();
}
