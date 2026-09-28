using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Common.Logging;

/// <summary>
/// El único lugar que registra el inicio, el fin y el fallo de una operación (Etapa 3, tarea 2).
/// <see cref="Handling"/> y <see cref="Handled"/> quedan disponibles sueltos para un punto de entrada que no devuelve un
/// <see cref="Result"/> (<c>WhatsAppWebhookService</c>, que devuelve <see langword="bool"/>).
/// </summary>
internal static partial class OperationLog
{
    /// <summary>
    /// Registra el inicio, corre <paramref name="work"/> entero (validar, abrir el límite, la cookie o el caché) y
    /// registra el fin o el código de error. Nunca registra el pedido. No atrapa excepciones: si el trabajo lanza, el
    /// log queda solo con "Handling" y la excepción sigue de largo.
    /// </summary>
    public static async Task<TResult> RunAsync<TResult>(ILogger logger, string operation, Func<Task<TResult>> work)
        where TResult : Result
    {
        Handling(logger, operation);

        var result = await work();

        if (result.IsSuccess)
        {
            Handled(logger, operation);
        }
        else
        {
            Failed(logger, operation, result.Error.Code);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Operation}")]
    internal static partial void Handling(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Operation}")]
    internal static partial void Handled(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} failed with {ErrorCode}")]
    internal static partial void Failed(ILogger logger, string operation, string errorCode);
}
