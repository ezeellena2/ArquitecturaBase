namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// Lo que la cola sabe del token: Meta lo rechazó o le faltan permisos, y todavía no salió ningún mensaje después. Lo
/// lee <see cref="WhatsAppHealthCheck"/> (sección 9 del spec: "error en el log y en la salud de la app").
/// </summary>
internal sealed class WhatsAppHealth
{
    private readonly Lock _lock = new();
    private WhatsAppSendFailure? _tokenProblem;

    /// <summary>
    /// <see cref="WhatsAppSendFailure.InvalidToken"/> o <see cref="WhatsAppSendFailure.MissingPermission"/>, el último
    /// que informó Meta; <c>null</c> si no hubo ninguno o si después salió un mensaje.
    /// </summary>
    public WhatsAppSendFailure? TokenProblem
    {
        get
        {
            lock (_lock)
            {
                return _tokenProblem;
            }
        }
    }

    public void ReportTokenProblem(WhatsAppSendFailure failure)
    {
        if (!failure.IsTokenProblem())
        {
            throw new ArgumentOutOfRangeException(nameof(failure), failure, "Only token problems affect the WhatsApp health.");
        }

        lock (_lock)
        {
            _tokenProblem = failure;
        }
    }

    /// <summary>
    /// Un envío aceptado prueba que el token vale otra vez: por ejemplo, si en Meta le asignaron la cuenta de WhatsApp al
    /// usuario del sistema, que no cambia el token. Un reinicio, en cambio, arranca sin problema anotado: esto vive en
    /// memoria.
    /// </summary>
    public void ReportSent()
    {
        lock (_lock)
        {
            _tokenProblem = null;
        }
    }
}
