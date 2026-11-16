using System.Net;
using System.Net.Sockets;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Domain.Delivery;
using ArquitecturaBase.Infrastructure.Emails;
using MailKit.Security;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

/// <summary>
/// N5 and N8 over the durable queue, with its own Api (and its own database): stopping the email worker while an email waits for
/// its retry or while it connects to a mail server that does not answer loses nothing and sends it once, and an invitation that
/// cannot go out before it expires is failed. Each test stops the Api's own worker and lets a copy of the Api take over.
/// </summary>
public sealed class EmailQueueShutdownTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_email_waiting_for_its_retry_survives_the_shutdown_and_another_copy_sends_it_once()
    {
        var to = TestEmails.Unique("espera");
        factory.EmailSender.Respond = (message, _) => Task.FromResult(message.To == to
            ? EmailSendResult.Failed(EmailSendOutcome.ConnectionFailed)
            : EmailSendResult.Sent($"<{message.Id:N}@tests.example>"));
        try
        {
            await EnqueueAsync(new EmailMessage(to, "Asunto", "<p>Hola</p>", "Hola"));
            await WaitUntilAsync(() => factory.EmailSender.AttemptsFor(to) == 1);
            await WaitUntilAsync(async () => (await RowsAsync()).Any(row => row.ClaimedAtUtc == null && row.AvailableAtUtc != null));

            await using (var worker = await StopTheWorkerAsync())
            {
                var waiting = Assert.Single(await RowsAsync());
                Assert.Null(waiting.ClaimedAtUtc);

                factory.EmailSender.Respond = null;
                await using var copy = factory.WithWebHostBuilder(_ => { });
                _ = copy.Services;
                factory.Clock.Advance(TimeSpan.FromMinutes(1));

                await factory.EmailSender.WaitForAsync(to);
                await WaitUntilAsync(async () => (await RowsAsync()).Count == 0);
            }

            Assert.Equal((2, 1), (factory.EmailSender.AttemptsFor(to), factory.EmailSender.CountFor(to)));
        }
        finally
        {
            factory.EmailSender.Respond = null;
        }
    }

    [Fact]
    public async Task An_email_cut_by_the_shutdown_while_it_connects_to_a_silent_server_goes_back_to_the_queue()
    {
        using var silentServer = new TcpListener(IPAddress.Loopback, 0);
        silentServer.Start();
        var connected = silentServer.AcceptTcpClientAsync(Ct).AsTask();
        var to = TestEmails.Unique("smtp-caido");

        await using (await StopTheWorkerAsync())
        {
            var smtp = new SmtpOptions
            {
                Host = IPAddress.Loopback.ToString(),
                Port = ((IPEndPoint)silentServer.LocalEndpoint).Port,
                Security = SecureSocketOptions.None,
                UserName = "user",
                Password = "password",
                FromName = "Acceso",
                FromAddress = "no-reply@example.test",
            };
            var copy = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddScoped<IEmailSender>(_ => new SmtpEmailSender(Options.Create(smtp), TimeSpan.FromSeconds(30)));
            }));
            TcpClient? silent = null;
            try
            {
                _ = copy.Services;
                await EnqueueAsync(new EmailMessage(to, "Asunto", "<p>Hola</p>", "Hola"));
                silent = await connected.WaitAsync(TimeSpan.FromSeconds(15), Ct);
                Assert.NotNull(Assert.Single(await RowsAsync()).ClaimedAtUtc);
            }
            finally
            {
                // The copy shuts down while its worker waits for the greeting; the server keeps the connection open and silent
                // until then, so what cuts the send is the shutdown and not a closed connection.
                await copy.DisposeAsync();
                silent?.Dispose();
            }

            var givenBack = Assert.Single(await RowsAsync());
            Assert.Equal(((DateTime?)null, (DateTime?)null), (givenBack.ClaimedAtUtc, givenBack.AvailableAtUtc));
        }

        // The Api's own worker is back and sends it, once.
        await factory.EmailSender.WaitForAsync(to);
        await WaitUntilAsync(async () => (await RowsAsync()).Count == 0);
        Assert.Equal(1, factory.EmailSender.CountFor(to));
    }

    [Fact]
    public async Task An_invitation_that_cannot_go_out_before_it_expires_is_failed()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("vence");
        factory.EmailSender.Respond = (message, _) => Task.FromResult(message.To == email
            ? EmailSendResult.Failed(EmailSendOutcome.AuthenticationFailed)
            : EmailSendResult.Sent($"<{message.Id:N}@tests.example>"));
        try
        {
            var userId = await admin.CreateOkAsync(new { email, displayName = "Laura", invitation = new { channel = "Email" } });
            await WaitUntilAsync(() => factory.EmailSender.AttemptsFor(email) == 1);
            await WaitUntilAsync(async () => (await RowsAsync()).Any(row => row.ClaimedAtUtc == null && row.AvailableAtUtc != null));
            Assert.Equal("Pending", (await admin.DetailAsync(userId)).GetProperty("lastInvitation").GetProperty("deliveryStatus").GetString());

            // A minute before its day ends, the next attempt fails too, and the one after it would come after its expiry.
            factory.Clock.Advance(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1));
            await WaitUntilAsync(async () => (await RowsAsync()).Count == 0);

            Assert.Equal(2, factory.EmailSender.AttemptsFor(email));
            // A day later the session of the administrator expired too.
            admin = await AdminUsersApi.SignInAsync(factory, client);
            var last = (await admin.DetailAsync(userId)).GetProperty("lastInvitation");
            Assert.Equal(("Email", "Failed"), (last.GetProperty("channel").GetString(), last.GetProperty("deliveryStatus").GetString()));
        }
        finally
        {
            factory.EmailSender.Respond = null;
        }
    }

    // An invitation whose row expired without any attempt (no copy ran during its day, or one abandoned its claim mid-send) is not
    // pending forever: once the queue cannot hold it any more, it may or may not have gone out.
    [Fact]
    public async Task An_invitation_whose_row_expired_without_news_is_uncertain()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("sin-copia");
        Guid userId;
        await using (await StopTheWorkerAsync())
        {
            userId = await admin.CreateOkAsync(new { email, displayName = "Laura", invitation = new { channel = "Email" } });
            Assert.Equal("Pending", (await admin.DetailAsync(userId)).GetProperty("lastInvitation").GetProperty("deliveryStatus").GetString());
            factory.Clock.Advance(QueuedDelivery.DefaultLifetime + TimeSpan.FromMinutes(1));
        }

        await WaitUntilAsync(async () => (await RowsAsync()).Count == 0);

        admin = await AdminUsersApi.SignInAsync(factory, client);
        Assert.Equal(0, factory.EmailSender.AttemptsFor(email));
        Assert.Equal("Uncertain", (await admin.DetailAsync(userId)).GetProperty("lastInvitation").GetProperty("deliveryStatus").GetString());
    }

    // N8: the host stops its workers at once and together, and the worst send plus what settles it fits in its shutdown.
    [Fact]
    public void The_longest_send_and_what_settles_it_fit_in_the_shutdown()
    {
        var host = factory.Services.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.True(host.ServicesStopConcurrently);
        Assert.True(SmtpEmailSender.DefaultDeadline + EmailBackgroundService.SettleTimeout + TimeSpan.FromSeconds(3) <= host.ShutdownTimeout);
    }

    /// <summary>Stops the Api's own email worker and starts it again when disposed.</summary>
    private async Task<IAsyncDisposable> StopTheWorkerAsync()
    {
        var worker = factory.Services.GetServices<IHostedService>().OfType<EmailBackgroundService>().Single();
        await worker.StopAsync(Ct);

        return new Restart(worker);
    }

    private Task EnqueueAsync(EmailMessage message) =>
        factory.InTransactionAsync(services =>
        {
            Assert.True(services.GetRequiredService<IEmailQueue>().TryEnqueue(message));
            return Task.CompletedTask;
        });

    private Task<List<QueuedDelivery>> RowsAsync() =>
        factory.ExecuteDbContextAsync(db => db.Set<QueuedDelivery>().AsNoTracking().Where(row => row.Channel == "email").ToListAsync(Ct));

    private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        while (!await condition())
        {
            await Task.Delay(50, timeout.Token);
        }
    }

    private sealed class Restart(IHostedService worker) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await worker.StartAsync(CancellationToken.None);
    }
}
