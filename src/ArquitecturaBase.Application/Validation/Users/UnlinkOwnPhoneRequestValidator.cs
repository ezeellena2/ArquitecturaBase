using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Validation.Users;

internal sealed class UnlinkOwnPhoneRequestValidator : AbstractValidator<UnlinkOwnPhoneRequest>
{
    public UnlinkOwnPhoneRequestValidator(IOptions<LoginCodeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Sólo hace falta con un correo verificado, y eso lo decide el caso de uso (N11).
        RuleFor(request => request.Code)
            .ValidLoginCode(options.Value.Length, _ => ValidationMessages.LoginCodeFormat)
            .When(request => request.Code is not null);
    }
}
