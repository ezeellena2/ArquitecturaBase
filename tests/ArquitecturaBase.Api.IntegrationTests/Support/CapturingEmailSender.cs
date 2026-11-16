using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Emails;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// IEmailSender de los tests: guarda los emails en memoria para leer el código enviado (sección 9). Un test con su propia
/// Api puede decidir cómo termina cada intento (<see cref="Respond"/>): sólo se guarda lo que sale.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentQueue<EmailMessage> _messages = new();
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);

    /// <summary>N5 y N8: cómo termina cada intento; null, sale siempre.</summary>
    public Func<EmailMessage, CancellationToken, Task<EmailSendResult>>? Respond { get; set; }

    /// <summary>El código es la primera palabra del asunto: "123456 es tu código de acceso a ...".</summary>
    public static string CodeOf(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Subject.Split(' ')[0];
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        _attempts.AddOrUpdate(message.To, 1, (_, previous) => previous + 1);

        var result = Respond is { } respond
            ? await respond(message, cancellationToken)
            : EmailSendResult.Sent($"<{message.Id:N}@tests.example>");

        if (result.Outcome == EmailSendOutcome.Sent)
        {
            _messages.Enqueue(message);
        }

        return result;
    }

    public int CountFor(string to) => _messages.Count(message => message.To == to);

    /// <summary>Cuántas veces se intentó mandar algo a <paramref name="to"/>, saliera o no.</summary>
    public int AttemptsFor(string to) => _attempts.GetValueOrDefault(to);

    /// <summary>Espera el email número <paramref name="number"/> (desde 1) enviado a <paramref name="to"/>.</summary>
    public async Task<EmailMessage> WaitForAsync(string to, int number = 1)
    {
        var testCancellation = TestContext.Current.CancellationToken;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
        timeout.CancelAfter(WaitTimeout);

        while (true)
        {
            var message = _messages.Where(candidate => candidate.To == to).Skip(number - 1).FirstOrDefault();

            if (message is not null)
            {
                return message;
            }

            try
            {
                await Task.Delay(20, timeout.Token);
            }
            catch (OperationCanceledException exception) when (!testCancellation.IsCancellationRequested)
            {
                // Solo el vencimiento de la espera; si se canceló el test, la cancelación se propaga tal cual.
                throw new TimeoutException(
                    $"Email #{number} to {to} did not arrive within {WaitTimeout.TotalSeconds} seconds.", exception);
            }
        }
    }
}
