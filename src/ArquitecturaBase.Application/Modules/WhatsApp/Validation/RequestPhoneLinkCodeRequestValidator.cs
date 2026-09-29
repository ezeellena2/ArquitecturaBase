using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

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
