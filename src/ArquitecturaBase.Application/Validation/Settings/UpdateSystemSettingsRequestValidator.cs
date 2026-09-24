using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Settings;

internal sealed class UpdateSystemSettingsRequestValidator : AbstractValidator<UpdateSystemSettingsRequest>
{
    public UpdateSystemSettingsRequestValidator() =>
        RuleFor(request => request.RegistrationMode)
            .IsInEnum()
            .WithMessage(_ => ValidationMessages.RegistrationModeInvalid);
}
