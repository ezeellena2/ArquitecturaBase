using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Settings.UpdateSystemSettings;

internal sealed class UpdateSystemSettingsCommandValidator : AbstractValidator<UpdateSystemSettingsCommand>
{
    // System.Text.Json acepta cualquier número para un enum: la lista blanca la pone el validador.
    public UpdateSystemSettingsCommandValidator() =>
        RuleFor(command => command.RegistrationMode)
            .IsInEnum()
            .WithMessage(_ => ValidationMessages.RegistrationModeInvalid);
}
