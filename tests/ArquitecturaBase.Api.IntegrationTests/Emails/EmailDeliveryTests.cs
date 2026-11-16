using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Infrastructure.Emails;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class EmailDeliveryTests
{
    private static readonly SmtpOptions Sender = new() { FromName = "Arquitectura Base", FromAddress = "no-reply@example.com" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Message_has_the_sender_the_recipient_and_both_bodies()
    {
        using var mime = MimeMessageFactory.Create(new EmailMessage("ana@example.com", "Asunto", "<p>Hola</p>", "Hola"), Sender);

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mime.From));
        Assert.Equal("no-reply@example.com", from.Address);
        Assert.Equal("Arquitectura Base", from.Name);
        Assert.Equal("ana@example.com", Assert.IsType<MailboxAddress>(Assert.Single(mime.To)).Address);
        Assert.Equal("Asunto", mime.Subject);
        Assert.Equal("<p>Hola</p>", mime.HtmlBody);
        Assert.Equal("Hola", mime.TextBody);
    }

    // The copy of a form action carries its PDF; the rest of the emails have no attachments.
    [Fact]
    public void Message_carries_its_attachments_with_their_name_type_and_bytes()
    {
        byte[] pdf = [37, 80, 68, 70];
        using var mime = MimeMessageFactory.Create(new EmailMessage("ana@example.com", "Factura C 0004-00000012", "<p>Hola</p>", "Hola")
        {
            Attachments = [new EmailAttachment("Factura C 0004-00000012.pdf", "application/pdf", pdf)],
        }, Sender);

        var attachment = Assert.IsType<MimePart>(Assert.Single(mime.Attachments));
        Assert.Equal(("Factura C 0004-00000012.pdf", "application/pdf"), (attachment.FileName, attachment.ContentType.MimeType));
        using var content = new MemoryStream();
        attachment.Content!.DecodeTo(content, Ct);
        Assert.Equal(pdf, content.ToArray());
        Assert.Equal(("<p>Hola</p>", "Hola"), (mime.HtmlBody, mime.TextBody));
    }

    [Fact]
    public async Task Pickup_directory_sender_saves_an_eml_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "arquitecturabase-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        try
        {
            var sender = new PickupDirectoryEmailSender(
                Options.Create(new EmailOptions { PickupDirectory = directory }),
                Options.Create(Sender),
                new TestHostEnvironment(),
                TimeProvider.System,
                NullLogger<PickupDirectoryEmailSender>.Instance);

            var result = await sender.SendAsync(new EmailMessage("ana@example.com", "123456 es tu código", "<p>123456</p>", "123456"), Ct);

            var file = Assert.Single(Directory.GetFiles(directory, "*.eml"));
            using var saved = await MimeMessage.LoadAsync(file, Ct);
            Assert.Equal("123456 es tu código", saved.Subject);
            Assert.Equal((EmailSendOutcome.Sent, saved.MessageId), (result.Outcome, result.MessageId));

            // N8: with the host stopping before it starts, nothing is written and the email goes back to the queue.
            using var stopped = new CancellationTokenSource();
            await stopped.CancelAsync();
            Assert.Equal(EmailSendOutcome.Stopped, (await sender.SendAsync(new EmailMessage("ana@example.com", "s", "b", "b"), stopped.Token)).Outcome);
            Assert.Single(Directory.GetFiles(directory, "*.eml"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Smtp_sender_gives_up_on_a_server_that_never_answers()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var accepted = server.AcceptTcpClientAsync(Ct).AsTask();
        var options = new SmtpOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = ((IPEndPoint)server.LocalEndpoint).Port,
            Security = SecureSocketOptions.None,
            UserName = "user",
            Password = "password",
            FromName = Sender.FromName,
            FromAddress = Sender.FromAddress,
        };
        var sender = new SmtpEmailSender(Options.Create(options), TimeSpan.FromSeconds(1));

        var sending = sender.SendAsync(new EmailMessage("ana@example.com", "Asunto", "<p>Hola</p>", "Hola"), Ct);
        var finished = await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(15), Ct));

        // N5: it never got to the body, so it is a connection failure that the queue retries.
        Assert.Same(sending, finished);
        Assert.Equal(EmailSendOutcome.ConnectionFailed, (await sending).Outcome);
        using var silent = await accepted;
    }

    // N8: the shutdown cuts the send while it connects, before the body: nothing went out and the email goes back to the queue.
    [Fact]
    public async Task Smtp_sender_stops_with_the_host_while_it_connects()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var accepted = server.AcceptTcpClientAsync(Ct).AsTask();
        var sender = new SmtpEmailSender(Options.Create(Smtp(((IPEndPoint)server.LocalEndpoint).Port)), TimeSpan.FromSeconds(30));
        using var stopping = new CancellationTokenSource();

        var sending = sender.SendAsync(new EmailMessage("ana@example.com", "Asunto", "<p>Hola</p>", "Hola"), stopping.Token);
        using var silent = await accepted;
        await stopping.CancelAsync();

        Assert.Equal(EmailSendOutcome.Stopped, (await sending.WaitAsync(TimeSpan.FromSeconds(10), Ct)).Outcome);
    }

    // N5 and N8: what the server answers decides what is retried. Before the body, the credentials (a 535) and a 4xx are retried;
    // after MAIL FROM, a 4xx is retried and a 5xx is not; a cut after the body may have arrived, so it is not repeated.
    [Theory]
    [InlineData("421 4.7.0 Try again later", "235 2.7.0 Accepted", "250 2.1.5 OK", EmailSendOutcome.TemporarilyRejected, 421)]
    [InlineData("220 localhost ESMTP", "535 5.7.8 Username and Password not accepted", "250 2.1.5 OK", EmailSendOutcome.AuthenticationFailed, null)]
    [InlineData("220 localhost ESMTP", "235 2.7.0 Accepted", "450 4.2.1 Mailbox busy", EmailSendOutcome.TemporarilyRejected, 450)]
    [InlineData("220 localhost ESMTP", "235 2.7.0 Accepted", "550 5.1.1 No such user", EmailSendOutcome.Rejected, 550)]
    public async Task Smtp_sender_classifies_what_the_server_answers(
        string greeting, string auth, string recipient, EmailSendOutcome expected, int? statusCode)
    {
        await using var server = new ScriptedSmtpServer(greeting, auth, recipient);
        var sender = new SmtpEmailSender(Options.Create(Smtp(server.Port)), TimeSpan.FromSeconds(10));

        var result = await sender.SendAsync(new EmailMessage("ana@example.com", "Asunto", "<p>Hola</p>", "Hola"), Ct);

        Assert.Equal((expected, statusCode, (string?)null), (result.Outcome, result.StatusCode, result.MessageId));
        Assert.False(server.ReceivedBody);
    }

    // What the server answers to MAIL FROM. A 530 asks for the credentials again: nothing went out, and it is retried as a 535
    // is. A 5xx to the sender, as Gmail's 550 5.4.5 once the daily quota is spent, refuses every email alike: it is not retried,
    // and the worker tells the health check (EmailBackgroundServiceTests).
    [Theory]
    [InlineData("530 5.7.0 Authentication Required", EmailSendOutcome.AuthenticationFailed, null, true)]
    [InlineData("550 5.4.5 Daily user sending limit exceeded", EmailSendOutcome.SenderRejected, 550, false)]
    [InlineData("421 4.7.0 Try again later", EmailSendOutcome.TemporarilyRejected, 421, true)]
    public async Task Smtp_sender_classifies_what_the_server_answers_to_the_sender(
        string answer, EmailSendOutcome expected, int? statusCode, bool retried)
    {
        await using var server = new ScriptedSmtpServer(sender: answer);
        var sender = new SmtpEmailSender(Options.Create(Smtp(server.Port)), TimeSpan.FromSeconds(10));

        var result = await sender.SendAsync(new EmailMessage("ana@example.com", "Asunto", "<p>Hola</p>", "Hola"), Ct);

        Assert.Equal((expected, statusCode, retried), (result.Outcome, result.StatusCode, result.IsRetryable));
        Assert.False(server.ReceivedBody);
    }

    // A 4xx after the final dot says the server did not take it (RFC 5321), so it is retried although the body went out.
    [Fact]
    public async Task Smtp_sender_retries_a_4xx_after_the_body()
    {
        await using var server = new ScriptedSmtpServer(afterBody: "451 4.3.0 Try again later");
        var sender = new SmtpEmailSender(Options.Create(Smtp(server.Port)), TimeSpan.FromSeconds(10));

        var result = await sender.SendAsync(new EmailMessage("ana@example.com", "Asunto", "<p>Hola</p>", "Hola"), Ct);

        Assert.True(server.ReceivedBody);
        Assert.Equal((EmailSendOutcome.TemporarilyRejected, 451, true), (result.Outcome, result.StatusCode, result.IsRetryable));
    }

    [Fact]
    public async Task Smtp_sender_takes_a_cut_after_the_body_as_uncertain()
    {
        await using var server = new ScriptedSmtpServer(afterBody: null);
        var sender = new SmtpEmailSender(Options.Create(Smtp(server.Port)), TimeSpan.FromSeconds(10));

        var result = await sender.SendAsync(new EmailMessage("ana@example.com", "Asunto", "<p>Hola</p>", "Hola"), Ct);

        Assert.True(server.ReceivedBody);
        Assert.Equal(EmailSendOutcome.Uncertain, result.Outcome);
    }

    // N5: an accepted email comes back with the Message-ID it went out with, which an invitation keeps as sent.
    [Fact]
    public async Task Smtp_sender_returns_the_message_id_the_server_accepted()
    {
        await using var server = new ScriptedSmtpServer();
        var sender = new SmtpEmailSender(Options.Create(Smtp(server.Port)), TimeSpan.FromSeconds(10));

        var result = await sender.SendAsync(new EmailMessage("ana@example.com", "Asunto", "<p>Hola</p>", "Hola"), Ct);

        Assert.Equal(EmailSendOutcome.Sent, result.Outcome);
        Assert.Equal($"<{result.MessageId}>", server.ReceivedMessageId);
    }

    private static SmtpOptions Smtp(int port) => new()
    {
        Host = IPAddress.Loopback.ToString(),
        Port = port,
        Security = SecureSocketOptions.None,
        UserName = "user",
        Password = "password",
        FromName = Sender.FromName,
        FromAddress = Sender.FromAddress,
    };

    [Fact]
    public void Registration_builds_the_smtp_sender_with_its_own_timeout()
    {
        using var provider = new ServiceCollection().AddSingleton(Options.Create(Sender)).BuildServiceProvider();

        Assert.IsType<SmtpEmailSender>(ActivatorUtilities.CreateInstance<SmtpEmailSender>(provider));
    }

    [Fact]
    public void Smtp_settings_are_required_only_when_sending_by_smtp()
    {
        var empty = new SmtpOptions();

        Assert.True(Validator(EmailDelivery.PickupDirectory).Validate(null, empty).Skipped);

        var result = Validator(EmailDelivery.Smtp).Validate(null, empty);
        Assert.True(result.Failed);
        Assert.Contains(nameof(SmtpOptions.Host), result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains(nameof(SmtpOptions.Password), result.FailureMessage, StringComparison.Ordinal);
    }

    private static SmtpOptionsValidator Validator(EmailDelivery delivery) =>
        new(Options.Create(new EmailOptions { Delivery = delivery }));

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
