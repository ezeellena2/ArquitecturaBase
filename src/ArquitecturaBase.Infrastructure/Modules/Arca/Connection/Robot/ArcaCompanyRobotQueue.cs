using System.Threading.Channels;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>
/// La cola en memoria de este host para el lado del cliente (ADR 0026): la clave nunca toca la base ni la red interna. Si el
/// host se reinicia, lo encolado se pierde y la reserva de la corrida vence: la pantalla la muestra interrumpida.
/// </summary>
internal sealed class ArcaCompanyRobotQueue(IOptions<ArcaRobotBrowserOptions> options) : IArcaCompanyRobotQueue
{
    private readonly Channel<(ArcaCompanyRobotWorkResponse Job, ArcaFiscalCredentials Credentials)> channel =
        Channel.CreateBounded<(ArcaCompanyRobotWorkResponse, ArcaFiscalCredentials)>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public bool TryEnqueue(ArcaCompanyRobotWorkResponse job, ArcaFiscalCredentials credentials) => channel.Writer.TryWrite((job, credentials));

    public IAsyncEnumerable<(ArcaCompanyRobotWorkResponse Job, ArcaFiscalCredentials Credentials)> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
