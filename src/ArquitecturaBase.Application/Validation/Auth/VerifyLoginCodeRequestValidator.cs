using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Resources;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Validation.Auth;

internal sealed class VerifyLoginCodeRequestValidator : AbstractValidator<VerifyLoginCodeRequest>
{
    public VerifyLoginCodeRequestValidator(IOptions<LoginCodeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var length = options.Value.Length;

        When(request => request.IsByPhone, () =>
                RuleFor(request => request.Phone)
                    .Must((request, _) => string.IsNullOrWhiteSpace(request.Email))
                    .WithMessage(_ => ValidationMessages.EmailOrPhone))
            .Otherwise(() => RuleFor(request => request.Email).ValidEmail());

        RuleFor(request => request.Code).ValidLoginCode(
            length, request => request.IsByPhone ? ValidationMessages.LoginCodeFormatWhatsApp : ValidationMessages.LoginCodeFormat);

        RuleFor(request => request.ReturnUrl)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(ReturnUrls.IsAuthorizeRequest)
            .WithMessage(_ => ValidationMessages.ReturnUrlInvalid);
    }
}
