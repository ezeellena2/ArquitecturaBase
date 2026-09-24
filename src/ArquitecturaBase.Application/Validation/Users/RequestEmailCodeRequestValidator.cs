using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Users;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class RequestEmailCodeRequestValidator : AbstractValidator<RequestEmailCodeRequest>
{
    public RequestEmailCodeRequestValidator() => RuleFor(request => request.Email).ValidEmail();
}
