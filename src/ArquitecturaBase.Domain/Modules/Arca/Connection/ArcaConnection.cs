using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>
/// La conexión de la instalación con ARCA: una sola (Scope = 1), con un ambiente y los servicios que eligió la empresa.
/// Está activa cuando la última prueba salió bien y la configuración de cada familia elegida está completa (ADR 0025).
/// En los modos de delegación la CUIT sigue siendo la de la empresa y el certificado es el de la plataforma, que firma
/// (ADR 0027).
/// </summary>
public sealed class ArcaConnection : Entity, IAuditable
{
    private ArcaConnection() { }

    public int Scope { get; private set; } = 1;
    public ArcaAccessMode Mode { get; private set; }
    public ArcaEnvironment Environment { get; private set; }
    public string Cuit { get; private set; } = string.Empty;

    /// <summary>La CUIT de la plataforma en los modos de delegación; null con el certificado de la empresa.</summary>
    public string? PlatformCuit { get; private set; }

    /// <summary>La CUIT del certificado: la de la plataforma en los modos de delegación, la de la empresa en el modo 1.</summary>
    public string SignerCuit => PlatformCuit ?? Cuit;
    public ArcaConnectionStatus Status { get; private set; }
    public string[] EnabledServices { get; private set; } = [];
    public bool? LastTestPassed { get; private set; }
    public DateTime? LastTestedAtUtc { get; private set; }
    public long Revision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static Result<ArcaConnection> Start(
        string cuit,
        ArcaEnvironment environment,
        IReadOnlyCollection<ArcaServiceCode> chosen,
        IReadOnlyCollection<ArcaServiceCode> installed,
        IReadOnlyCollection<ArcaServiceCode> required) =>
        Create(ArcaAccessMode.CompanyCertificate, cuit, platformCuit: null, environment, chosen, installed, required);

    /// <summary>
    /// El alta en modo delegación: la empresa delega sus servicios en la CUIT de la plataforma, que llega validada desde la
    /// configuración de la instalación. La guiada y la del robot (ADR 0026) comparten el modelo; cambia sólo el modo.
    /// </summary>
    public static Result<ArcaConnection> StartDelegated(
        string cuit,
        string platformCuit,
        ArcaEnvironment environment,
        IReadOnlyCollection<ArcaServiceCode> chosen,
        IReadOnlyCollection<ArcaServiceCode> installed,
        IReadOnlyCollection<ArcaServiceCode> required,
        ArcaAccessMode mode = ArcaAccessMode.PlatformDelegation)
    {
        if (mode is not (ArcaAccessMode.PlatformDelegation or ArcaAccessMode.PlatformDelegationAutomated))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Only the delegation modes start a delegated connection.");
        var platform = CuitNumber.Normalize(platformCuit)
            ?? throw new ArgumentException("The platform CUIT must come validated from the configuration.", nameof(platformCuit));
        return Create(mode, cuit, platform, environment, chosen, installed, required);
    }

    /// <summary>Reemplaza los servicios elegidos; los obligatorios quedan siempre.</summary>
    public Result ChooseServices(
        IReadOnlyCollection<ArcaServiceCode> chosen,
        IReadOnlyCollection<ArcaServiceCode> installed,
        IReadOnlyCollection<ArcaServiceCode> required)
    {
        if (Services(chosen, installed, required) is not { } services) return ArcaConnectionErrors.ServiceNotInstalled;
        EnabledServices = services;
        Advance();
        return Result.Success();
    }

    /// <summary>Se pidió otro certificado: una renovación, o un pedido nuevo mientras se espera el primero.</summary>
    public void CertificateRequested() => Advance();

    /// <summary>Se canceló el pedido de certificado pendiente.</summary>
    public void RequestCancelled() => Advance();

    /// <summary>El primer certificado quedó cargado. Uno de renovación no cambia el estado hasta pasar la prueba.</summary>
    public void CertificateLoaded()
    {
        if (Status == ArcaConnectionStatus.AwaitingCertificate) Status = ArcaConnectionStatus.Unverified;
        Advance();
    }

    public void RecordTest(bool passed, bool familiesComplete, DateTime testedAtUtc)
    {
        if (Status == ArcaConnectionStatus.AwaitingCertificate)
            throw new InvalidOperationException("An ARCA connection is tested only after its certificate is loaded.");
        LastTestPassed = passed;
        LastTestedAtUtc = testedAtUtc;
        Evaluate(familiesComplete);
    }

    /// <summary>Vuelve a calcular el estado cuando una familia completó o perdió su configuración, sin otra prueba.</summary>
    public void ReevaluateFamilies(bool familiesComplete) => Evaluate(familiesComplete);

    public void CertificateExpired()
    {
        if (Status is ArcaConnectionStatus.Active or ArcaConnectionStatus.Unverified) Status = ArcaConnectionStatus.NeedsAttention;
        Advance();
    }

    public override string ToString() => $"ArcaConnection {{ cuit: {CuitNumber.Mask(Cuit)}, status: {Status} }}";

    private void Evaluate(bool familiesComplete)
    {
        if (Status != ArcaConnectionStatus.AwaitingCertificate)
        {
            Status = (LastTestPassed, familiesComplete) switch
            {
                (true, true) => ArcaConnectionStatus.Active,
                _ when Status is ArcaConnectionStatus.Active or ArcaConnectionStatus.NeedsAttention => ArcaConnectionStatus.NeedsAttention,
                _ => ArcaConnectionStatus.Unverified,
            };
        }
        Advance();
    }

    private void Advance() => Revision = checked(Revision + 1);

    private static Result<ArcaConnection> Create(
        ArcaAccessMode mode,
        string cuit,
        string? platformCuit,
        ArcaEnvironment environment,
        IReadOnlyCollection<ArcaServiceCode> chosen,
        IReadOnlyCollection<ArcaServiceCode> installed,
        IReadOnlyCollection<ArcaServiceCode> required)
    {
        if (CuitNumber.Normalize(cuit) is not { } normalized) return ArcaConnectionErrors.CuitInvalid;
        if (Services(chosen, installed, required) is not { } services) return ArcaConnectionErrors.ServiceNotInstalled;
        return new ArcaConnection
        {
            Mode = mode,
            Environment = environment,
            Cuit = normalized,
            PlatformCuit = platformCuit,
            Status = ArcaConnectionStatus.AwaitingCertificate,
            EnabledServices = services,
            Revision = 1,
        };
    }

    private static string[]? Services(
        IReadOnlyCollection<ArcaServiceCode> chosen,
        IReadOnlyCollection<ArcaServiceCode> installed,
        IReadOnlyCollection<ArcaServiceCode> required)
    {
        if (chosen.Any(service => !installed.Contains(service))) return null;
        return [.. required.Concat(chosen).Select(service => service.Value).Distinct(StringComparer.Ordinal)];
    }
}
