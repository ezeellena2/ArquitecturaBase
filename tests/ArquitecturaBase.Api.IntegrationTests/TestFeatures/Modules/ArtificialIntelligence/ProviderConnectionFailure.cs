using System.Net.Sockets;
using System.Text;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

/// <summary>
/// I6: the handler the registration configures, whose connection never reaches a provider: the name does not resolve, the connection is
/// refused, TLS fails against a peer that answers plain text, or the connect timeout expires (shortened here; the registration's own
/// value is checked apart). No network and nothing billable; <see cref="Attempts"/> counts the connections it tried.
/// </summary>
internal sealed class ProviderConnectionFailure(string fault)
{
    private int attempts;
    public int Attempts => attempts;

    public SocketsHttpHandler Handler()
    {
        var handler = AiAssistantProvidersRegistration.ProviderHandler();
        if (fault == "connectTimeout") handler.ConnectTimeout = TimeSpan.FromMilliseconds(200);
        handler.ConnectCallback = async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref attempts);
            switch (fault)
            {
                case "refused": throw new SocketException((int)SocketError.ConnectionRefused);
                case "name": throw new SocketException((int)SocketError.HostNotFound);
                case "tls": return new PlainTextPeer();
                case "connectTimeout":
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("The connect timeout did not expire.");
                default: throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
            }
        };
        return handler;
    }

    /// <summary>A peer that answers plain HTTP to a TLS handshake: it reads its text once and discards what is written.</summary>
    private sealed class PlainTextPeer() : MemoryStream(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n"))
    {
        public override void Write(byte[] buffer, int offset, int count) { }
        public override void Write(ReadOnlySpan<byte> buffer) { }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.CompletedTask;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
