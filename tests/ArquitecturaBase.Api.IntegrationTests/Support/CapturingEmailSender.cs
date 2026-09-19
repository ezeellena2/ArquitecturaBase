using System.Collections.Concurrent;
using ArquitecturaBase.Application.Abstractions.Emails;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>IEmailSender de los tests: guarda los emails en memoria para leer el código enviado (sección 9).</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    /// <summary>El código es la primera palabra del asunto: "123456 es tu código de acceso a ...".</summary>
    public static string CodeOf(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Subject.Split(' ')[0];
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);

        return Task.CompletedTask;
    }

    public int CountFor(string to) => _messages.Count(message => message.To == to);

    /// <summary>Espera el email número <paramref name="number"/> (desde 1) enviado a <paramref name="to"/>.</summary>
    public async Task<EmailMessage> WaitForAsync(string to, int number = 1)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (true)
        {
            var message = _messages.Where(candidate => candidate.To == to).Skip(number - 1).FirstOrDefault();

            if (message is not null)
            {
                return message;
            }

            await Task.Delay(20, timeout.Token);
        }
    }
}
