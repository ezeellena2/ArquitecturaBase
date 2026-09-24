using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Validation.Auth;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users.RequestPhoneLinkCode;

/// <summary>
/// Solo la forma del pedido, como el del código para entrar. Si el número es un celular, y de qué país, lo decide el
/// caso de uso con el parser, que responde <c>Users.Phone.Invalid</c> o <c>Auth.WhatsApp.CountryNotSupported</c>.
/// </summary>
internal sealed class RequestPhoneLinkCodeCommandValidator : AbstractValidator<RequestPhoneLinkCodeCommand>
{
    public RequestPhoneLinkCodeCommandValidator()
    {
        RuleFor(command => command.Number)
            .Cascade(CascadeMode.Stop)
            .Required()
            .MaxLength(RequestWhatsAppLoginCodeRequestValidator.NumberMaxLength);

        RuleFor(command => command.Country).OptionalCountry();
    }
}
