using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;

/// <summary>
/// Solo la forma del pedido. Si el número es un celular, y de qué país, lo decide el caso de uso con el parser, que
/// responde <c>Users.Phone.Invalid</c> o <c>Auth.WhatsApp.CountryNotSupported</c>.
/// </summary>
internal sealed class RequestWhatsAppLoginCodeCommandValidator : AbstractValidator<RequestWhatsAppLoginCodeCommand>
{
    /// <summary>Un número con todos sus separadores ("+54 9 (351) 15 555-1234") entra con lugar de sobra.</summary>
    public const int NumberMaxLength = 32;

    public RequestWhatsAppLoginCodeCommandValidator()
    {
        RuleFor(command => command.Number)
            .Cascade(CascadeMode.Stop)
            .Required()
            .MaxLength(NumberMaxLength);

        // Opcional: solo hace falta para leer un número que no empieza con "+".
        RuleFor(command => command.Country).OptionalCountry();
    }
}
