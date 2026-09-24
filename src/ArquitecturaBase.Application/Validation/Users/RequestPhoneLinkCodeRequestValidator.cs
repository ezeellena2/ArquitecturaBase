using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Validation.Auth;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class RequestPhoneLinkCodeRequestValidator : AbstractValidator<RequestPhoneLinkCodeRequest>
{
    public RequestPhoneLinkCodeRequestValidator()
    {
        RuleFor(request => request.Number)
            .Cascade(CascadeMode.Stop)
            .Required()
            .MaxLength(RequestWhatsAppLoginCodeRequestValidator.NumberMaxLength);

        RuleFor(request => request.Country).OptionalCountry();
    }
}
