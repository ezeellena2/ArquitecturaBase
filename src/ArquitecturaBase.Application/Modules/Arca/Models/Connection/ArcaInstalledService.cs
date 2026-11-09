using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// Un servicio de ARCA que trae una familia instalada; los obligatorios quedan elegidos siempre. <paramref name="Delegated"/>
/// dice si, en los modos de delegación, la empresa lo delega en la plataforma porque actúa en su nombre (Facturación), o
/// si es una consulta que la plataforma hace con su propia CUIT (la constancia de inscripción, ADR 0027).
/// </summary>
public sealed record ArcaInstalledService(ArcaServiceCode Code, string Family, bool Required, bool Delegated = true);
