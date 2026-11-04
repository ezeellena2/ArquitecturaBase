using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>
/// La conexión de la instalación con ARCA: una sola (Scope = 1), con un ambiente y los servicios que eligió la empresa.
/// Está activa cuando la última prueba salió bien y la configuración de cada familia elegida está completa (ADR 0025).
/// </summary>
public sealed class ArcaConnection : Entity, IAuditable
{
    private ArcaConnection() { }

    public int Scope { get; private set; } = 1;
    public ArcaAccessMode Mode { get; private set; }
    public ArcaEnvironment Environment { get; private set; }
    public string Cuit { get; private set; } = string.Empty;
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
        IReadOnlyCollection<ArcaServiceCode> required)
    {
        if (CuitNumber.Normalize(cuit) is not { } normalized) return ArcaConnectionErrors.CuitInvalid;
        if (Services(chosen, installed, required) is not { } services) return ArcaConnectionErrors.ServiceNotInstalled;
        return new ArcaConnection
        {
            Mode = ArcaAccessMode.CompanyCertificate,
            Environment = environment,
            Cuit = normalized,
            Status = ArcaConnectionStatus.AwaitingCertificate,
            EnabledServices = services,
            Revision = 1,
        };
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

    private static string[]? Services(
        IReadOnlyCollection<ArcaServiceCode> chosen,
        IReadOnlyCollection<ArcaServiceCode> installed,
        IReadOnlyCollection<ArcaServiceCode> required)
    {
        if (chosen.Any(service => !installed.Contains(service))) return null;
        return [.. required.Concat(chosen).Select(service => service.Value).Distinct(StringComparer.Ordinal)];
    }
}
