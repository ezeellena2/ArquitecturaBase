using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Features.Settings.UpdateSystemSettings;

public sealed record UpdateSystemSettingsCommand(RegistrationMode RegistrationMode) : ICommand;
