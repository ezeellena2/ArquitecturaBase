using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// El lado del cliente que corre el host que recibió la clave: la corrida y su reserva, la CUIT que delega, la de la
/// plataforma y los servicios. <paramref name="DelegatedServices"/> son los que se delegan; <paramref name="EnabledServices"/>,
/// todos los elegidos, para los pasos de cada familia.
/// </summary>
public sealed record ArcaCompanyRobotWorkResponse(Guid RunId, Guid Owner, string CompanyCuit, string PlatformCuit,
    IReadOnlyList<string> DelegatedServices, IReadOnlyList<string> EnabledServices)
{
    public override string ToString() => $"ArcaCompanyRobotWorkResponse {{ run: {RunId}, company: {CuitNumber.Mask(CompanyCuit)} }}";
}

/// <summary>
/// El lado de la plataforma, durable: con <paramref name="CertificateRequestPem"/> el robot crea el alias y baja el
/// certificado (RB5-A); sin él, el certificado ya está cargado. Acepta y asigna sólo si <paramref name="CompanyDone"/>.
/// <paramref name="AuthorizedServices"/> son las consultas que la plataforma autoriza para su alias con su propia CUIT.
/// </summary>
public sealed record ArcaPlatformRobotWorkResponse(Guid RunId, Guid Owner, string CompanyCuit, string PlatformCuit, string Alias,
    string? CertificateRequestPem, IReadOnlyList<string> CompletedSteps, bool CompanyDone, IReadOnlyList<string> DelegatedServices,
    IReadOnlyList<string> AuthorizedServices)
{
    public override string ToString() => $"ArcaPlatformRobotWorkResponse {{ run: {RunId}, company: {CuitNumber.Mask(CompanyCuit)} }}";
}

/// <summary>Cómo terminó una pasada del robot: bien, o frenado en un paso por un motivo estable.</summary>
public abstract record ArcaRobotOutcome
{
    private ArcaRobotOutcome() { }

    public static ArcaRobotOutcome Success { get; } = new Succeeded();

    public static ArcaRobotOutcome Stopped(ArcaRobotFailure failure, string step) => new Failed(failure, step);

    public sealed record Succeeded : ArcaRobotOutcome;

    public sealed record Failed(ArcaRobotFailure Failure, string Step) : ArcaRobotOutcome;
}
