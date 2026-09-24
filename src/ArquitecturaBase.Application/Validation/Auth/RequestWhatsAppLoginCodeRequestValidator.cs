using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Auth;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Auth;

/// <summary>Valida la forma; el parser y la configuración deciden si el celular y su país son admitidos.</summary>
internal sealed class RequestWhatsAppLoginCodeRequestValidator : AbstractValidator<RequestWhatsAppLoginCodeRequest>
{
    public const int NumberMaxLength = 32;

    public RequestWhatsAppLoginCodeRequestValidator()
    {
        RuleFor(request => request.Number)
            .Cascade(CascadeMode.Stop)
            .Required()
            .MaxLength(NumberMaxLength);

        RuleFor(request => request.Country).OptionalCountry();
    }
}
