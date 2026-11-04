using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>Un servicio de ARCA que trae una familia instalada; los obligatorios quedan elegidos siempre.</summary>
public sealed record ArcaInstalledService(ArcaServiceCode Code, string Family, bool Required);
