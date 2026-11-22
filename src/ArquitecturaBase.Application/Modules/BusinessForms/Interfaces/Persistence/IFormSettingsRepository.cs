using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>The row of Configuración › Formularios, followed to save it under its lock; null while nobody saved it.</summary>
public interface IFormSettingsRepository
{
    Task LockAsync(CancellationToken cancellationToken);
    Task<BusinessFormsSettings?> GetAsync(CancellationToken cancellationToken);
    void Add(BusinessFormsSettings settings);
}
