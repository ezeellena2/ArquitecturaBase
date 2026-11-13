using System.Text;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>
/// El robot de delegación con Playwright y Chromium (ADR 0026). Cada pasada abre un navegador sin perfil y sin trazas, HAR,
/// video ni capturas, con un plazo total, y lo cierra siempre. La clave del cliente sólo llega al campo de ingreso; la de la
/// plataforma sale de la configuración. Los pasos que ya están hechos no se repiten, y un lado sin nada pendiente no abre el
/// navegador.
/// </summary>
internal sealed partial class PlaywrightArcaDelegationRobot(IEnumerable<IArcaRobotCompanyStep> companySteps, IOptions<ArcaRobotBrowserOptions> options,
    ILogger<PlaywrightArcaDelegationRobot> logger) : IArcaDelegationRobot, IAsyncDisposable
{
    private readonly SemaphoreSlim start = new(1, 1);
    private IPlaywright? playwright;

    public Task<ArcaRobotOutcome> RunCompanyAsync(ArcaCompanyRobotWorkResponse job, ArcaFiscalCredentials credentials, IArcaRobotProgress progress,
        CancellationToken cancellationToken) =>
        RunAsync(progress, ArcaRobotSteps.CompanyLogin, async (session, steps, token) =>
        {
            await steps.RunAsync(ArcaRobotSteps.CompanyLogin, async () =>
            {
                await session.LoginAsync(options.Value.LoginUrl, credentials.LoginCuit, credentials.Key.Reveal);
                return null;
            });

            foreach (var service in job.DelegatedServices)
            {
                await steps.RunAsync(ArcaRobotSteps.CompanyDelegate(Code(service)), async () =>
                {
                    await DelegateAsync(session, service, job.CompanyCuit, job.PlatformCuit);
                    return null;
                });
            }

            foreach (var step in companySteps.Where(step => step.AppliesTo(job.EnabledServices)))
                await steps.RunAsync(step.Key, () => step.RunAsync(session, job, token));
        }, cancellationToken);

    public async Task<ArcaRobotOutcome> RunPlatformAsync(ArcaPlatformRobotWorkResponse job, IArcaRobotProgress progress, CancellationToken cancellationToken)
    {
        bool Done(string step) => job.CompletedSteps.Contains(step, StringComparer.Ordinal);
        var authorize = job.AuthorizedServices.Where(service => !Done(ArcaRobotSteps.PlatformAuthorize(Code(service)))).ToArray();
        var accept = job.CompanyDone && !Done(ArcaRobotSteps.PlatformAccept);
        var assign = job.CompanyDone ? job.DelegatedServices.Where(service => !Done(ArcaRobotSteps.PlatformAssign(Code(service)))).ToArray() : [];

        // El certificado cargado a mano o en otra pasada cuenta como hecho, sin entrar a ARCA.
        if (job.CertificateRequestPem is null && !Done(ArcaRobotSteps.PlatformCertificate)
            && !await progress.CompleteAsync(ArcaRobotSteps.PlatformCertificate, null, cancellationToken))
            return ArcaRobotOutcome.Stopped(ArcaRobotFailure.Interrupted, ArcaRobotSteps.PlatformCertificate);
        if (job.CertificateRequestPem is null && authorize.Length == 0 && !accept && assign.Length == 0) return ArcaRobotOutcome.Success;

        var platform = options.Value.Platform;
        return await RunAsync(progress, ArcaRobotSteps.PlatformLogin, async (session, steps, _) =>
        {
            await steps.RunAsync(ArcaRobotSteps.PlatformLogin, async () =>
            {
                await session.LoginAsync(options.Value.LoginUrl, CuitNumber.Normalize(platform.LoginCuit!)!, () => platform.FiscalKey!);
                return null;
            });

            if (job.CertificateRequestPem is { } request)
            {
                await steps.RunAsync(ArcaRobotSteps.PlatformCertificate, async () =>
                {
                    var certificate = await CreateCertificateAsync(session, job.PlatformCuit, job.Alias, request);
                    if (!await progress.InstallCertificateAsync(certificate, cancellationToken))
                        throw new ArcaRobotStoppedException(ArcaRobotFailure.ScreenChanged);
                    return null;
                });
            }

            foreach (var service in authorize)
            {
                await steps.RunAsync(ArcaRobotSteps.PlatformAuthorize(Code(service)), async () =>
                {
                    await AssignToComputerAsync(session, service, job.PlatformCuit, job.PlatformCuit, job.Alias);
                    return null;
                });
            }

            if (accept)
            {
                await steps.RunAsync(ArcaRobotSteps.PlatformAccept, async () =>
                {
                    await AcceptAsync(session, job);
                    return null;
                });
            }

            foreach (var service in assign)
            {
                await steps.RunAsync(ArcaRobotSteps.PlatformAssign(Code(service)), async () =>
                {
                    await AssignToComputerAsync(session, service, job.PlatformCuit, job.CompanyCuit, job.Alias);
                    return null;
                });
            }
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        playwright?.Dispose();
        start.Dispose();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Abre el navegador, corre los pasos y lo cierra. Un motivo del robot o una reserva perdida terminan la pasada con su
    /// paso; el plazo total vencido es ARCA sin responder. Una excepción de otro tipo es un bug y sube.
    /// </summary>
    private async Task<ArcaRobotOutcome> RunAsync(IArcaRobotProgress progress, string firstStep,
        Func<ArcaRobotSession, StepRunner, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Value.RunTimeout);
        var steps = new StepRunner(progress, firstStep, deadline.Token);
        try
        {
            var browser = await (await PlaywrightAsync()).Chromium.LaunchAsync(new() { Headless = options.Value.Headless });
            await using (browser)
            {
                // Sin RecordHar, RecordVideo, tracing ni capturas: nada de lo que pasa por la página queda escrito.
                var context = await browser.NewContextAsync(new() { AcceptDownloads = true, Locale = "es-AR", TimezoneId = "America/Argentina/Buenos_Aires" });
                await using (context)
                {
                    context.SetDefaultTimeout((float)options.Value.StepTimeout.TotalMilliseconds);
                    context.SetDefaultNavigationTimeout((float)options.Value.StepTimeout.TotalMilliseconds);
                    var page = await context.NewPageAsync();
                    await work(new ArcaRobotSession(context, page), steps, deadline.Token);
                }
            }

            return ArcaRobotOutcome.Success;
        }
        catch (ArcaRobotStoppedException stopped)
        {
            LogStopped(stopped.Failure, steps.Current);
            return ArcaRobotOutcome.Stopped(stopped.Failure, steps.Current);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogStopped(ArcaRobotFailure.Unavailable, steps.Current);
            return ArcaRobotOutcome.Stopped(ArcaRobotFailure.Unavailable, steps.Current);
        }
        catch (PlaywrightException exception)
        {
            // El navegador no arrancó o se cayó: no es la pantalla de ARCA. Se registra el tipo, nunca el mensaje.
            LogBrowserFailed(exception.GetType().Name, steps.Current);
            return ArcaRobotOutcome.Stopped(ArcaRobotFailure.Unavailable, steps.Current);
        }
    }

    private async Task<IPlaywright> PlaywrightAsync()
    {
        if (playwright is not null) return playwright;
        await start.WaitAsync();
        try
        {
            return playwright ??= await Playwright.CreateAsync();
        }
        finally
        {
            start.Release();
        }
    }

    /// <summary>«Nueva relación»: delega el servicio de la empresa en la CUIT de la plataforma (etapa 5, company.delegate).</summary>
    private async Task DelegateAsync(ArcaRobotSession session, string service, string companyCuit, string platformCuit)
    {
        await session.OpenServiceAsync(ArcaRobotScreens.RelationsService, companyCuit);
        await session.ClickAsync(ArcaRobotScreens.NewRelation);
        await session.SelectIfPresentAsync(ArcaRobotScreens.RepresentedSelect, ArcaRobotScreens.Cuit(companyCuit));
        await ChooseServiceAsync(session, service);
        await session.ClickInRowAsync(ArcaRobotScreens.Loose("Representante"), ArcaRobotScreens.Search);
        await session.FillAsync(ArcaRobotScreens.RepresentativeCuit, platformCuit);
        await session.ClickAsync(ArcaRobotScreens.Search);
        await ConfirmRelationAsync(session);
    }

    /// <summary>
    /// «Nueva relación» del lado de la plataforma, con el computador fiscal de la instalación como representante: para sus
    /// propias consultas (platform.authorize) o, representando a la empresa, para lo que delegó (platform.assign).
    /// </summary>
    private async Task AssignToComputerAsync(ArcaRobotSession session, string service, string platformCuit, string representedCuit, string alias)
    {
        await session.OpenServiceAsync(ArcaRobotScreens.RelationsService, platformCuit);
        await session.ClickAsync(ArcaRobotScreens.NewRelation);
        if (representedCuit != platformCuit) await session.SelectAsync(ArcaRobotScreens.RepresentedSelect, ArcaRobotScreens.Cuit(representedCuit));
        else await session.SelectIfPresentAsync(ArcaRobotScreens.RepresentedSelect, ArcaRobotScreens.Cuit(platformCuit));
        await ChooseServiceAsync(session, service);
        await session.ClickInRowAsync(ArcaRobotScreens.Loose("Representante"), ArcaRobotScreens.Search);
        await session.SelectAsync(ArcaRobotScreens.FiscalComputerSelect, ArcaRobotScreens.Loose(alias, exact: true));
        await session.ClickAsync(ArcaRobotScreens.Search);
        await ConfirmRelationAsync(session);
    }

    /// <summary>Acepta, en las autorizaciones pendientes, cada servicio que la empresa delegó; sin pendientes, ya estaba hecho.</summary>
    private async Task AcceptAsync(ArcaRobotSession session, ArcaPlatformRobotWorkResponse job)
    {
        await session.OpenServiceAsync(ArcaRobotScreens.RelationsService, job.PlatformCuit);
        await session.ClickAsync(ArcaRobotScreens.PendingRelations, exact: false);
        foreach (var service in job.DelegatedServices)
        {
            var row = session.Rows(ArcaRobotScreens.Cuit(job.CompanyCuit)).Filter(new() { HasTextRegex = ArcaRobotScreens.Loose(Label(service)) });
            if (await row.CountAsync() == 0) continue;
            await row.First.GetByRole(AriaRole.Button, new() { NameRegex = ArcaRobotScreens.Loose(ArcaRobotScreens.Accept, exact: true) }).ClickAsync();
            await session.ClickIfPresentAsync(ArcaRobotScreens.Confirm);
            await session.ExpectAsync(ArcaRobotScreens.Pattern("aceptad|constancia"));
            await session.ClickIfPresentAsync("Volver");
        }
    }

    /// <summary>
    /// Crea el alias con el pedido de la app en «Administración de Certificados Digitales», o le agrega un certificado si el
    /// alias ya existe, y baja el certificado nuevo (RB5-A). El pedido es público; la clave privada no sale del servidor.
    /// </summary>
    private static async Task<string> CreateCertificateAsync(ArcaRobotSession session, string platformCuit, string alias, string requestPem)
    {
        await session.OpenServiceAsync(ArcaRobotScreens.CertificatesService, platformCuit);
        var request = Encoding.ASCII.GetBytes(requestPem);
        var existing = session.Rows(ArcaRobotScreens.Loose(alias));
        if (await existing.CountAsync() > 0)
        {
            await existing.First.GetByRole(AriaRole.Link, new() { NameRegex = ArcaRobotScreens.Loose(ArcaRobotScreens.View, exact: true) })
                .Or(existing.First.GetByRole(AriaRole.Button, new() { NameRegex = ArcaRobotScreens.Loose(ArcaRobotScreens.View, exact: true) }))
                .First.ClickAsync();
            await session.SetFileAsync(ArcaRobotScreens.RequestInput, $"{alias}.csr", "application/pkcs10", request);
            await session.ClickAsync(ArcaRobotScreens.AddCertificate);
        }
        else
        {
            await session.ClickAsync(ArcaRobotScreens.AddAlias);
            await session.FillAsync(ArcaRobotScreens.AliasInput, alias);
            await session.SetFileAsync(ArcaRobotScreens.RequestInput, $"{alias}.csr", "application/pkcs10", request);
            await session.ClickAsync(ArcaRobotScreens.AddAlias);
        }

        var certificate = await session.DownloadTextAsync(ArcaRobotScreens.Download);
        if (!certificate.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)) throw new ArcaRobotStoppedException(ArcaRobotFailure.ScreenChanged);
        return certificate;
    }

    /// <summary>El «BUSCAR» de «Servicio»: organismo y tipo cuando ARCA los pregunta, y el servicio por su nombre.</summary>
    private async Task ChooseServiceAsync(ArcaRobotSession session, string service)
    {
        await session.ClickInRowAsync(ArcaRobotScreens.Loose("Servicio"), ArcaRobotScreens.Search);
        await session.ClickIfPresentAsync(ArcaRobotScreens.Organism);
        await session.ClickIfPresentAsync(ArcaRobotScreens.WebServices);
        await session.ClickAsync(Label(service));
    }

    private static async Task ConfirmRelationAsync(ArcaRobotSession session)
    {
        await session.ClickAsync(ArcaRobotScreens.Confirm);
        await session.ExpectAsync(ArcaRobotScreens.Pattern(ArcaRobotScreens.RelationDone));
    }

    private string Label(string service) => options.Value.ServiceLabels.TryGetValue(service, out var label) ? label : service;

    private static ArcaServiceCode Code(string service) =>
        ArcaServiceCode.Create(service) ?? throw new InvalidOperationException("The ARCA robot got a service code that is not valid.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ARCA robot stopped at {Step}: {Failure}.")]
    private partial void LogStopped(ArcaRobotFailure failure, string step);

    [LoggerMessage(Level = LogLevel.Error, Message = "The ARCA robot browser failed with {ExceptionType} at {Step}.")]
    private partial void LogBrowserFailed(string exceptionType, string step);

    /// <summary>Corre cada paso entre su aviso de inicio y de fin; una reserva perdida detiene la pasada sin guardar más.</summary>
    private sealed class StepRunner(IArcaRobotProgress progress, string first, CancellationToken cancellationToken)
    {
        public string Current { get; private set; } = first;

        public async Task RunAsync(string step, Func<Task<string?>> action)
        {
            Current = step;
            cancellationToken.ThrowIfCancellationRequested();
            if (!await progress.BeginAsync(step, cancellationToken)) throw new ArcaRobotStoppedException(ArcaRobotFailure.Interrupted);
            var output = await action();
            if (!await progress.CompleteAsync(step, output, cancellationToken)) throw new ArcaRobotStoppedException(ArcaRobotFailure.Interrupted);
        }
    }
}
