using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Emails;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// La cola de correo real, que se puede cerrar: mientras acepta, registra y pasa cada correo a la cola real. Un test la
/// pone con ConfigureTestServices, sobre la <c>EmailQueue</c> registrada.
/// </summary>
internal sealed class SwitchableEmailQueue : IEmailQueue
{
    private IEmailQueue? _inner;

    public bool Accepts { get; set; } = true;

    public ConcurrentQueue<EmailMessage> Queued { get; } = new();

    public SwitchableEmailQueue Over(IEmailQueue inner)
    {
        _inner = inner;
        return this;
    }

    public bool TryEnqueue(EmailMessage message)
    {
        if (!Accepts)
        {
            return false;
        }

        Queued.Enqueue(message);

        return _inner?.TryEnqueue(message) ?? true;
    }
}
