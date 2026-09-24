using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users.RequestEmailCode;

internal sealed class RequestEmailCodeCommandValidator : AbstractValidator<RequestEmailCodeCommand>
{
    public RequestEmailCodeCommandValidator() => RuleFor(command => command.Email).ValidEmail();
}
