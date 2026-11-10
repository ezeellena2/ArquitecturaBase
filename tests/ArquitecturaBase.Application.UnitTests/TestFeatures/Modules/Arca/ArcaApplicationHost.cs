using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;

/// <summary>
/// Los servicios reales del módulo con la base, ARCA y las familias en memoria: el padrón obligatorio y la facturación
/// opcional, cada una con su prueba y su completitud programables. Con <c>withPlatform</c>, la instalación tiene
/// configurada la CUIT de la plataforma para el modo delegación; con <c>withRobot</c>, también el robot prendido, con la
/// dirección del aviso, y sus lados, la cola del host, los correos y el operador en memoria.
/// </summary>
internal sealed class ArcaApplicationHost : IDisposable
{
    public static readonly DateTime NowUtc = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    public const string PlatformCuit = "30719998883";
    public const string PlatformName = "Plataforma Ejemplo S.A.";

    public const string AlertEmail = "plataforma@ejemplo.com";

    public ArcaApplicationHost(bool withPlatform = false, bool withRobot = false, string? alertEmail = AlertEmail)
    {
        var settings = new Dictionary<string, string?>();
        if (withPlatform || withRobot)
        {
            settings["Arca:Platform:Cuit"] = "30-71999888-3";
            settings["Arca:Platform:Name"] = PlatformName;
        }

        if (withRobot)
        {
            settings["Arca:Robot:Enabled"] = "true";
            settings["Arca:Robot:AlertEmail"] = alertEmail;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging().AddApplication().AddArcaApplication();
        services.AddSingleton<IConfiguration>(configuration);
        services.RemoveAll<IArcaServiceDefinition>();
        services.AddSingleton<IArcaServiceDefinition>(RegistryFamily);
        services.AddSingleton<IArcaServiceDefinition>(InvoicingFamily);
        services.AddSingleton<IUnitOfWork>(Work);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IArcaConnectionRepository>(Store);
        services.AddSingleton<IArcaConnectionReader>(Store);
        services.AddSingleton<IArcaInvoicingRepository>(Store);
        services.AddSingleton<IArcaInvoicingReader>(Store);
        services.AddSingleton<IArcaCredentialService>(Credentials);
        services.AddSingleton<IArcaAccessTicketProvider>(Tickets);
        services.AddSingleton<IArcaInvoicingClient>(Invoicing);
        services.AddSingleton<IArcaRobotRunRepository>(Runs);
        services.AddSingleton<IArcaRobotRunReader>(Runs);
        services.AddSingleton<IArcaCompanyRobotQueue>(RobotQueue);
        services.AddSingleton<IEmailQueue>(Emails);
        services.AddSingleton<ICurrentUser>(User);
        Store.InTransaction = () => Work.InTransaction;
        Runs.InTransaction = () => Work.InTransaction;
        Provider = services.BuildServiceProvider();
    }

    public FakeUnitOfWork Work { get; } = new();
    public FakeArcaStore Store { get; } = new();
    public FakeArcaCredentials Credentials { get; } = new();
    public FakeArcaTickets Tickets { get; } = new();
    public FakeArcaInvoicingClient Invoicing { get; } = new();
    public FakeArcaRobotRuns Runs { get; } = new();
    public FakeArcaRobotQueue RobotQueue { get; } = new();
    public FakeEmailQueue Emails { get; } = new();
    public FakeCurrentUser User { get; } = new() { UserId = Guid.CreateVersion7() };
    public FakeArcaDefinition RegistryFamily { get; } = new("ws_sr_constancia_inscripcion", "Registry", required: true, delegated: false);
    public FakeArcaDefinition InvoicingFamily { get; } = new("wsfe", "Invoicing", required: false);
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(NowUtc));
    public ServiceProvider Provider { get; }

    public IArcaConnectionService Connection => Provider.GetRequiredService<IArcaConnectionService>();
    public IArcaConnectionTestService Test => Provider.GetRequiredService<IArcaConnectionTestService>();
    public IArcaRobotService Robot => Provider.GetRequiredService<IArcaRobotService>();
    public IArcaRobotRunStore RobotStore => Provider.GetRequiredService<IArcaRobotRunStore>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>El alta hasta el certificado subido: la conexión queda sin verificar, con el certificado activo.</summary>
    public async Task<ArcaConnectionResponse> StartAndUploadAsync(params string[] services)
    {
        var started = await Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", services, 0, ArcaAccessMode.CompanyCertificate, Ct);
        return (await Connection.UploadCertificateAsync("CERT", started.Value.Revision, Ct)).Value;
    }

    /// <summary>Una conexión activa: el alta, la prueba en verde y las familias completas.</summary>
    public async Task<ArcaConnectionResponse> ActivateAsync(params string[] services)
    {
        await StartAndUploadAsync(services);
        var tested = await Test.TestAsync(Ct);
        Assert.Equal(ArcaConnectionStatus.Active, tested.Value.Connection.Status);
        return tested.Value.Connection;
    }

    /// <summary>El alta en modo 3, con el pedido del certificado de la plataforma pendiente (ADR 0026).</summary>
    public async Task<ArcaConnectionResponse> StartAutomatedAsync(params string[] services) =>
        (await Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Production, "plataforma-demo", services, 0,
            ArcaAccessMode.PlatformDelegationAutomated, Ct)).Value;

    /// <summary>Una renovación con su certificado ya cargado, pendiente de la prueba.</summary>
    public async Task<ArcaConnectionResponse> LoadRenewalAsync(ArcaConnectionResponse active)
    {
        var requested = await Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "renovacion", [], active.Revision, ArcaAccessMode.CompanyCertificate, Ct);
        return (await Connection.UploadCertificateAsync("RENEWED", requested.Value.Revision, Ct)).Value;
    }

    public void Dispose() => Provider.Dispose();
}

internal sealed class FakeArcaCredentials : IArcaCredentialService
{
    /// <summary>Lo que responde el inspector en lugar de aceptar el certificado.</summary>
    public Error? InspectError { get; set; }

    /// <summary>Las CUIT con que se pidieron e inspeccionaron los certificados, en orden.</summary>
    public List<string> RequestedCuits { get; } = [];
    public List<string> InspectedCuits { get; } = [];

    public ArcaCertificateRequest CreateRequest(Guid connectionId, Guid certificateId, string cuit, string alias)
    {
        RequestedCuits.Add(cuit);
        return new("CSR:" + alias, $"protected:{certificateId}");
    }

    public Result<ArcaInspectedCertificate> Inspect(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem,
        string cuit, ArcaEnvironment environment, DateTime nowUtc)
    {
        InspectedCuits.Add(cuit);
        if (InspectError is { } error) return error;
        return new ArcaInspectedCertificate("PEM:" + certificatePem, nowUtc.AddDays(-1), nowUtc.AddYears(2), $"THUMB:{certificateId}");
    }

    public string SignLoginRequest(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem, byte[] loginTicketRequest) =>
        throw new NotSupportedException("The application services never sign; the ticket provider does.");

    public string ProtectTicket(Guid connectionId, Guid certificateId, ArcaServiceCode service, string token, string sign) =>
        throw new NotSupportedException("The application services never store tickets.");

    public (string Token, string Sign)? UnprotectTicket(Guid connectionId, Guid certificateId, ArcaServiceCode service, string protectedCredentials) =>
        throw new NotSupportedException("The application services never read tickets.");
}

internal sealed class FakeArcaTickets : IArcaAccessTicketProvider
{
    public ArcaTicketOutcome Outcome { get; set; } = new ArcaTicketOutcome.Ready("token", "sign", ArcaApplicationHost.NowUtc.AddHours(12));

    public Task<ArcaTicketOutcome> GetAsync(ArcaCertificateRef certificate, ArcaServiceCode service, CancellationToken cancellationToken) =>
        Task.FromResult(Outcome);
}

internal sealed class FakeArcaInvoicingClient : IArcaInvoicingClient
{
    public IReadOnlyList<ArcaPointOfSale> Points { get; set; } = [];
    public ArcaServiceCheckStatus Status { get; set; } = ArcaServiceCheckStatus.Ready;

    public Task<ArcaServiceReply<bool>> CheckServerAsync(ArcaEnvironment environment, CancellationToken cancellationToken) =>
        Task.FromResult(ArcaServiceReply.Ready(true));

    public Task<ArcaServiceReply<IReadOnlyList<ArcaPointOfSale>>> ListPointsOfSaleAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        CancellationToken cancellationToken) =>
        Task.FromResult(Status == ArcaServiceCheckStatus.Ready ? ArcaServiceReply.Ready(Points) : ArcaServiceReply.Failed<IReadOnlyList<ArcaPointOfSale>>(Status));

    public Task<ArcaServiceReply<long>> GetLastAuthorizedAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket, int pointOfSale,
        int voucherType, CancellationToken cancellationToken) => throw new NotSupportedException("The setup services never issue vouchers.");

    public Task<ArcaServiceReply<ArcaAuthorization>> RequestAuthorizationAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        ArcaVoucherRequest voucher, CancellationToken cancellationToken) => throw new NotSupportedException("The setup services never issue vouchers.");

    public Task<ArcaServiceReply<ArcaIssuedVoucher?>> FindVoucherAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket, int pointOfSale,
        int voucherType, long number, CancellationToken cancellationToken) => throw new NotSupportedException("The setup services never issue vouchers.");

    public Task<ArcaServiceReply<IReadOnlyList<ArcaParameter>>> ListParametersAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        ArcaParameterTable table, CancellationToken cancellationToken) => throw new NotSupportedException("The setup services never read ARCA tables.");
}

/// <summary>Una familia de prueba: su resultado, su completitud y lo que pasa mientras prueba se programan desde el test.</summary>
internal sealed class FakeArcaDefinition(string code, string family, bool required, bool delegated = true) : IArcaServiceDefinition
{
    public ArcaServiceCode Code { get; } = ArcaServiceCode.Create(code)!.Value;
    public IReadOnlyCollection<ArcaInstalledService> Services => [new(Code, family, required, delegated)];
    public ArcaServiceCheckStatus Status { get; set; } = ArcaServiceCheckStatus.Ready;
    public Func<bool> Complete { get; set; } = () => true;
    public Action? OnCheck { get; set; }
    public List<Guid> CheckedCertificates { get; } = [];
    public ArcaCertificateRef? LastCertificate { get; private set; }

    public Task<ArcaServiceCheckResponse> CheckAsync(ArcaServiceCode service, ArcaServiceContext context, CancellationToken cancellationToken)
    {
        CheckedCertificates.Add(context.Certificate.Id);
        LastCertificate = context.Certificate;
        OnCheck?.Invoke();
        return Task.FromResult(Status == ArcaServiceCheckStatus.Ready
            ? ArcaServiceCheckResponse.Passed(service, ArcaApplicationHost.NowUtc.AddHours(12))
            : ArcaServiceCheckResponse.Failed(service, Status, null));
    }

    public Task<bool> IsCompleteAsync(Guid connectionId, CancellationToken cancellationToken) => Task.FromResult(Complete());
}
