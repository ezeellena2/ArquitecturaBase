using System.Globalization;
using System.Net;
using System.Resources;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Modules.Arca.Configuration;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// El aviso al dueño de la plataforma cuando el robot se rompe (RB4-A): una pantalla cambiada de cualquier lado, y del lado
/// de la plataforma también un captcha, una validación extra o su ingreso rechazado. Un ingreso rechazado del cliente o ARCA
/// sin responder no avisan. Como mucho uno por motivo y paso dentro del lapso; el correo va por la cola transaccional, en la
/// transacción de quien lo llama, con el paso, el motivo y la hora, sin datos del cliente. El log de error sale siempre.
/// </summary>
internal sealed partial class ArcaRobotAlertPolicy(IEmailQueue emailQueue, IOptions<ArcaRobotOptions> options, ILogger<ArcaRobotAlertPolicy> logger)
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Modules.Arca.Resources.Robot", typeof(ArcaRobotAlertPolicy).Assembly);

    // El robot es de ARCA, de la Argentina: el aviso sale en el idioma de la plataforma, no en el del pedido que lo disparó.
    private const string Language = "es";
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo(Language);

    public static bool Applies(ArcaRobotSide side, ArcaRobotFailure failure) =>
        failure == ArcaRobotFailure.ScreenChanged
        || (side == ArcaRobotSide.Platform && failure is ArcaRobotFailure.Captcha or ArcaRobotFailure.ExtraValidation or ArcaRobotFailure.LoginRejected);

    public void Notify(ArcaRobotRun run, ArcaRobotOutcome.Failed failed, DateTime nowUtc)
    {
        LogStopped(run.Side, failed.Failure, failed.Step);
        if (!Applies(run.Side, failed.Failure)) return;

        var key = $"{failed.Failure}:{failed.Step}";
        if (!run.ShouldAlert(key, nowUtc, options.Value.AlertInterval)) return;
        if (string.IsNullOrWhiteSpace(options.Value.AlertEmail)) return;
        if (!emailQueue.TryEnqueue(Compose(options.Value.AlertEmail.Trim(), run.Side, failed, nowUtc)))
        {
            LogAlertNotQueued(failed.Failure, failed.Step);
            return;
        }

        run.Alerted(key, nowUtc);
    }

    private static EmailMessage Compose(string to, ArcaRobotSide side, ArcaRobotOutcome.Failed failed, DateTime nowUtc)
    {
        var body = DisplayNumbers.Format(Language, Text("Alert.Body"), failed.Step, Text("Side." + side), Text("Failure." + failed.Failure),
            nowUtc.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        return new EmailMessage(to, Text("Alert.Subject"), $"<p>{WebUtility.HtmlEncode(body)}</p>", body);
    }

    private static string Text(string key) => ResourceManager.GetString(key, Culture)
        ?? throw new InvalidOperationException("The ARCA robot text is missing.");

    [LoggerMessage(Level = LogLevel.Error, Message = "The ARCA delegation robot stopped on the {Side} side at {Step}: {Failure}.")]
    private partial void LogStopped(ArcaRobotSide side, ArcaRobotFailure failure, string step);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ARCA robot alert for {Failure} at {Step} was not queued; the log is the only notice.")]
    private partial void LogAlertNotQueued(ArcaRobotFailure failure, string step);
}
