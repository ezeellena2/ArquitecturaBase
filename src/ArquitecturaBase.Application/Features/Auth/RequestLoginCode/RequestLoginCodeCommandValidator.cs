using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

internal sealed class RequestLoginCodeCommandValidator : AbstractValidator<RequestLoginCodeCommand>
{
    public RequestLoginCodeCommandValidator() => RuleFor(command => command.Email).ValidEmail();
}
