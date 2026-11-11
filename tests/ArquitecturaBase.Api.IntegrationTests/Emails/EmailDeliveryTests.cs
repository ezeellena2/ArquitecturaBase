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

            await sender.SendAsync(new EmailMessage("ana@example.com", "123456 es tu código", "<p>123456</p>", "123456"), Ct);

            var file = Assert.Single(Directory.GetFiles(directory, "*.eml"));
            using var saved = await MimeMessage.LoadAsync(file, Ct);
            Assert.Equal("123456 es tu código", saved.Subject);
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

        Assert.Same(sending, finished);
        Assert.True(sending.IsFaulted);
        using var silent = await accepted;
    }

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
