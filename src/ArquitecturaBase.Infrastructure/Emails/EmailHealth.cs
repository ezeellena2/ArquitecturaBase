using ArquitecturaBase.Application.Models.Emails;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>Qué le pasa al servidor de correo, para <see cref="EmailHealthCheck"/>.</summary>
internal enum EmailHealthProblem
{
    /// <summary>Rechazó las credenciales (un 535): todos los correos van a fallar igual hasta que alguien las arregle.</summary>
    CredentialsRejected,

    /// <summary>No se pudo llegar, o rechazó por un rato o rechazó al remitente en los últimos intentos seguidos.</summary>
    ServerUnavailable,
}

/// <summary>
/// N5: lo que la app sabe del servidor de correo por los últimos intentos del worker. Un 535 lo marca enseguida; la conexión,
/// los 4xx o el remitente rechazado (el cupo diario de Gmail), a los <see cref="FailuresBeforeUnavailable"/> intentos seguidos,
/// porque uno suelto es un corte de un momento. Un envío que sale lo limpia; un rechazo de un correo puntual (un 5xx a su
/// destinatario) o un envío incierto no dicen nada del servidor. Vive en memoria: un reinicio arranca sin problema anotado.
/// </summary>
internal sealed class EmailHealth
{
    public const int FailuresBeforeUnavailable = 3;

    private readonly Lock _lock = new();
    private bool _credentialsRejected;
    private int _failuresInARow;

    public EmailHealthProblem? Problem
    {
        get
        {
            lock (_lock)
            {
                return _credentialsRejected ? EmailHealthProblem.CredentialsRejected
                    : _failuresInARow >= FailuresBeforeUnavailable ? EmailHealthProblem.ServerUnavailable : null;
            }
        }
    }

    public void ReportSent()
    {
        lock (_lock)
        {
            _credentialsRejected = false;
            _failuresInARow = 0;
        }
    }

    /// <summary>Una falla del servidor y no del correo: la conexión, las credenciales, un 4xx o el remitente rechazado.</summary>
    public void ReportFailure(EmailSendOutcome outcome)
    {
        lock (_lock)
        {
            if (outcome == EmailSendOutcome.AuthenticationFailed) _credentialsRejected = true;
            _failuresInARow++;
        }
    }
}
