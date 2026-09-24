using ArquitecturaBase.Application.Interfaces.Integrations;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>Se decide una vez, al registrar los servicios, según haya o no <c>Authentication:Google:ClientId</c>.</summary>
internal sealed record GoogleAvailability(bool IsEnabled) : IGoogleAvailability;
