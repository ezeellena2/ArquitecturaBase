using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Auth;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Auth;

internal sealed class RequestLoginCodeRequestValidator : AbstractValidator<RequestLoginCodeRequest>
{
    public RequestLoginCodeRequestValidator() => RuleFor(request => request.Email).ValidEmail();
}
