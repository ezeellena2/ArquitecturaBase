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
    /// <summary>RC7-B: «Avisar N días antes» del vencimiento del certificado, de 1 a 365; 30 mientras nadie lo cambie.</summary>
    public const int DefaultCertificateNoticeDays = 30;
    public const int MinCertificateNoticeDays = 1;
    public const int MaxCertificateNoticeDays = 365;

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

    /// <summary>
    /// RC7-B: desde cuántos días antes del vencimiento del certificado activo la pantalla lo avisa y sale el correo a quienes
    /// administran la conexión. Es configuración de la conexión: se guarda con su revisión.
    /// </summary>
    public int CertificateNoticeDays { get; private set; } = DefaultCertificateNoticeDays;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static Result<ArcaConnection> Start(
        string cuit,
        ArcaEnvironment environment,
        IReadOnlyCollection<ArcaServiceCode> chosen,
        IReadOnlyCollection<ArcaServiceCode> installed,
        IReadOnlyCollection<ArcaServiceCode> required,
        long firstRevision = 1) =>
        Create(ArcaAccessMode.CompanyCertificate, cuit, platformCuit: null, environment, chosen, installed, required, firstRevision);

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
        ArcaAccessMode mode = ArcaAccessMode.PlatformDelegation,
        long firstRevision = 1)
    {
        if (mode is not (ArcaAccessMode.PlatformDelegation or ArcaAccessMode.PlatformDelegationAutomated))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Only the delegation modes start a delegated connection.");
        var platform = CuitNumber.Normalize(platformCuit)
            ?? throw new ArgumentException("The platform CUIT must come validated from the configuration.", nameof(platformCuit));
        return Create(mode, cuit, platform, environment, chosen, installed, required, firstRevision);
    }

    /// <summary>
    /// Reemplaza los servicios elegidos; los obligatorios quedan siempre. Un servicio que se suma a una conexión activa la deja
    /// «requiere atención» hasta que una prueba dé verde, porque antes hay que autorizarlo en ARCA; no se prueba solo (B4).
    /// </summary>
    public Result ChooseServices(
        IReadOnlyCollection<ArcaServiceCode> chosen,
        IReadOnlyCollection<ArcaServiceCode> installed,
        IReadOnlyCollection<ArcaServiceCode> required)
    {
        if (Services(chosen, installed, required) is not { } services) return ArcaConnectionErrors.ServiceNotInstalled;
        var added = services.Except(EnabledServices, StringComparer.Ordinal).Any();
        EnabledServices = services;
        if (added && Status == ArcaConnectionStatus.Active) (Status, LastTestPassed) = (ArcaConnectionStatus.NeedsAttention, null);
        Advance();
        return Result.Success();
    }

    public static bool IsValidCertificateNotice(int days) => days is >= MinCertificateNoticeDays and <= MaxCertificateNoticeDays;

    /// <summary>RC7-B: cambia los días del aviso de vencimiento, sin tocar el estado.</summary>
    public Result ChangeCertificateNotice(int days)
    {
        if (!IsValidCertificateNotice(days)) return ArcaConnectionErrors.CertificateNoticeInvalid;
        CertificateNoticeDays = days;
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

    /// <summary>El certificado activo venció: una conexión activa requiere atención; en el alta, sigue sin verificar (B4).</summary>
    public void CertificateExpired()
    {
        if (Status == ArcaConnectionStatus.Active) Status = ArcaConnectionStatus.NeedsAttention;
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
        IReadOnlyCollection<ArcaServiceCode> required,
        long firstRevision)
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
            Revision = firstRevision,
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
