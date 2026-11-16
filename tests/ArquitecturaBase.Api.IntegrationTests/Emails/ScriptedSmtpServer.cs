using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

/// <summary>
/// N5 and N8: an SMTP server on the loopback that answers one connection with a script, so the sender's classification is tested
/// against real protocol answers: the greeting, the answer to AUTH, to MAIL FROM and to RCPT TO, and what it does after the body (an answer, or
/// cutting the connection). It writes down the Message-ID it received.
/// </summary>
internal sealed class ScriptedSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _serving;

    public ScriptedSmtpServer(
        string greeting = "220 localhost ESMTP",
        string auth = "235 2.7.0 Accepted",
        string recipient = "250 2.1.5 OK",
        string? afterBody = "250 2.0.0 OK queued",
        string sender = "250 2.1.0 OK")
    {
        _listener.Start();
        _serving = ServeAsync(greeting, auth, sender, recipient, afterBody);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string? ReceivedMessageId { get; private set; }

    public bool ReceivedBody { get; private set; }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try
        {
            await _serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or TimeoutException)
        {
        }
        _listener.Dispose();
    }

    private async Task ServeAsync(string greeting, string auth, string sender, string recipient, string? afterBody)
    {
        using var client = await _listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync(greeting);
        while (await reader.ReadLineAsync() is { } line)
        {
            var command = line.ToUpperInvariant();
            if (command.StartsWith("EHLO", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("250-localhost");
                await writer.WriteLineAsync("250 AUTH PLAIN");
            }
            else if (command.StartsWith("AUTH PLAIN", StringComparison.Ordinal))
            {
                if (command.Trim() == "AUTH PLAIN")
                {
                    await writer.WriteLineAsync("334 ");
                    await reader.ReadLineAsync();
                }
                await writer.WriteLineAsync(auth);
            }
            else if (command.StartsWith("MAIL FROM", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync(sender);
            }
            else if (command.StartsWith("RCPT TO", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync(recipient);
            }
            else if (command == "DATA")
            {
                await writer.WriteLineAsync("354 Go ahead");
                while (await reader.ReadLineAsync() is { } data && data != ".")
                {
                    if (data.StartsWith("Message-Id:", StringComparison.OrdinalIgnoreCase)) ReceivedMessageId = data[11..].Trim();
                }
                ReceivedBody = true;
                if (afterBody is null) return;
                await writer.WriteLineAsync(afterBody);
            }
            else if (command == "QUIT")
            {
                await writer.WriteLineAsync("221 2.0.0 Bye");
                return;
            }
            else
            {
                await writer.WriteLineAsync("250 2.0.0 OK");
            }
        }
    }
}
