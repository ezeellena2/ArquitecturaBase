namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>El resultado de probar la conexión: uno por servicio elegido y la conexión como quedó.</summary>
public sealed record ArcaTestResponse(IReadOnlyList<ArcaServiceCheckResponse> Checks, ArcaConnectionResponse Connection);
