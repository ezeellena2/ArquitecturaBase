using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>
/// RC6-B: los ajustes con un nombre, un remitente y un logo fijos, para los tests de correo que no abren la base. Sin branding
/// (<see cref="Unavailable"/>) la lectura falla, como una base caída.
/// </summary>
internal sealed class FixedBrandingReader(SystemBrandingResponse? branding) : ISystemSettingsReader
{
    public static FixedBrandingReader Unavailable { get; } = new(null);

    public static FixedBrandingReader Of(string appName = "Arquitectura Base", string senderName = "Arquitectura Base", string? logoVersion = null) =>
        new(new SystemBrandingResponse(appName, senderName, logoVersion, null));

    public int Reads { get; private set; }

    public Task<SystemBrandingResponse> FindBrandingAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return branding is null ? throw new InvalidOperationException("The settings could not be read.") : Task.FromResult(branding);
    }

    public Task<RegistrationMode> FindRegistrationModeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<SystemPresentationResponse> FindPresentationAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IdentityRetentionResponse> FindIdentityRetentionAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
