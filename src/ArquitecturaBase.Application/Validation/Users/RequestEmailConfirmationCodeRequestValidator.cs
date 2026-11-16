using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class RequestEmailConfirmationCodeRequestValidator : AbstractValidator<RequestEmailConfirmationCodeRequest>
{
    public RequestEmailConfirmationCodeRequestValidator()
    {
        RuleFor(request => request.Change)
            .NotNull().WithMessage(_ => ValidationMessages.Required)
            .IsInEnum().WithMessage(_ => ValidationMessages.Required);
    }
}
