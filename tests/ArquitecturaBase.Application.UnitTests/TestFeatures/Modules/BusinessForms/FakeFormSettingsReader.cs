using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

/// <summary>Configuración › Formularios (RC4-A) as a test sets it: today's values until it changes them.</summary>
internal sealed class FakeFormSettingsReader : IFormSettingsReader
{
    public FormSettingsResponse Settings { get; set; } = FormSettingsResponse.From(BusinessFormsSettings.Create());
    public int Reads { get; private set; }
    public Task<FormSettingsResponse> FindAsync(CancellationToken cancellationToken) { Reads++; return Task.FromResult(Settings); }
}
