using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Settings.GetSystemSettings;

public sealed record GetSystemSettingsQuery : IQuery<SystemSettingsResponse>;
