using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppFormActionDispatchTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private DateTime Now => factory.Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task A_known_message_id_is_committed_even_when_shutdown_arrives_after_the_provider_response()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct); var run = Guid.NewGuid();
        await scenario.ScopeAsync(services => Channel(services).EnqueueAsync(new(run, recipient.UserId, "Visita recibida"), Ct));
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        scenario.Cloud.BeforeReturn = shutdown.Cancel;
        await scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(shutdown.Token);
        var result = await scenario.ScopeAsync(services => Channel(services).GetAsync(run, recipient.UserId, Ct));
        Assert.Equal(StructuredRecordActionState.Accepted, result.State);
        Assert.Single(scenario.Cloud.Messages);
        Assert.Equal(1, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().CountAsync(Ct)));
    }

    [Fact]
    public async Task A_rejected_document_reuploads_the_same_private_copy_and_never_repeats_a_confirmed_message()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct); var run = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("%PDF-1.7\nPrivate fixed document fixture\n%%EOF"); var key = "private/action-" + run.ToString("N") + ".pdf";
        scenario.Files.Content[key] = content;
        var document = new StructuredRecordActionDocument(key, "Visita aprobada.pdf", "application/pdf", content.Length, Convert.ToHexString(SHA256.HashData(content)));
        await scenario.ScopeAsync(services => Channel(services).EnqueueAsync(new(run, recipient.UserId, "Tu visita aprobada", document), Ct));
        scenario.Cloud.Respond = () => scenario.Cloud.Messages.Count == 1 ? WhatsAppSendResult.Failed(WhatsAppSendFailure.Other) : WhatsAppSendResult.Sent("wamid.confirmed-document");
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await worker.ProcessNextAsync(Ct);
        Assert.Equal(StructuredRecordActionState.Failed, (await scenario.ScopeAsync(services => Channel(services).GetAsync(run, recipient.UserId, Ct))).State);
        Assert.Equal(StructuredRecordActionState.Pending, (await scenario.ScopeAsync(services => Channel(services).RetryAsync(run, recipient.UserId, 2, Ct))).State);
        await worker.ProcessNextAsync(Ct);
        Assert.Equal(content, scenario.Media.LastContent);
        Assert.Equal(2, scenario.Media.Calls);
        var sent = Assert.IsType<WhatsAppDocumentMessage>(scenario.Cloud.Messages[1]);
        Assert.Equal("2", sent.MediaId); Assert.Equal(document.Name, sent.Name); Assert.Equal("Tu visita aprobada", sent.Caption);
        Assert.Equal(StructuredRecordActionState.Accepted, (await scenario.ScopeAsync(services => Channel(services).RetryAsync(run, recipient.UserId, 2, Ct))).State);
        await worker.ProcessNextAsync(Ct);
        Assert.Equal(2, scenario.Cloud.Messages.Count); Assert.Equal(2, scenario.Media.Calls);
    }

    [Fact]
    public async Task An_unknown_message_outcome_only_consults_durable_state_and_never_sends_again()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct); var run = Guid.NewGuid();
        scenario.Cloud.Respond = WhatsAppSendResult.Uncertain;
        await scenario.ScopeAsync(services => Channel(services).EnqueueAsync(new(run, recipient.UserId, "Visita recibida"), Ct));
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>(); await worker.ProcessNextAsync(Ct);
        var status = await scenario.ScopeAsync(services => Channel(services).GetAsync(run, recipient.UserId, Ct));
        Assert.Equal(StructuredRecordActionState.Uncertain, status.State); Assert.False(status.CanRetry);
        Assert.Equal(StructuredRecordActionState.Uncertain, (await scenario.ScopeAsync(services => Channel(services).RetryAsync(run, recipient.UserId, 2, Ct))).State);
        await worker.ProcessNextAsync(Ct); Assert.Single(scenario.Cloud.Messages);
    }

    [Fact]
    public async Task Independent_hosts_accept_one_run_per_recipient_and_a_phone_change_cannot_create_a_second_send()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        await using var second = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", scenario.ConnectionString));
        var recipient = await scenario.RecipientAsync(Now, Ct); var request = new StructuredRecordActionMessage(Guid.NewGuid(), recipient.UserId, "Visita recibida");
        await using var secondScope = second.Services.CreateAsyncScope();
        var results = await Task.WhenAll(scenario.ScopeAsync(services => Channel(services).EnqueueAsync(request, Ct)),
            Channel(secondScope.ServiceProvider).EnqueueAsync(request, Ct));
        Assert.All(results, result => Assert.Equal(StructuredRecordActionState.Pending, result.State));
        await scenario.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>(); var old = await db.Set<WhatsAppContact>().SingleAsync(Ct); old.UnlinkUser();
            var phone = PhoneNumber.Create("+" + MetaWebhook.UniqueWaId()).Value;
            await services.GetRequiredService<IUserRepository>().SetPhoneAsync(recipient.UserId, phone, true, Ct);
            var contact = WhatsAppContact.Create(phone.Value[1..], MetaWebhook.UniqueBsuid(), "Nueva conversación", Now); contact.LinkUser(recipient.UserId);
            db.Set<WhatsAppContact>().Add(contact); return true;
        }, Ct);
        Assert.Equal(StructuredRecordActionState.Pending, (await scenario.ScopeAsync(services => Channel(services).EnqueueAsync(request, Ct))).State);
        var row = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking().SingleAsync(Ct));
        Assert.Equal(recipient.ContactId, row.ContactId);
        Assert.Equal(request.RunId, row.RequestId);
    }

    [Fact]
    public async Task Reservation_skips_a_contact_held_by_an_old_worker_without_locking_its_pending_outgoing()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        await using var second = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", scenario.ConnectionString));
        var recipient = await scenario.RecipientAsync(Now, Ct); var run = Guid.NewGuid();
        await scenario.ScopeAsync(services => Channel(services).EnqueueAsync(new(run, recipient.UserId, "Visita recibida"), Ct));
        var contactLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = scenario.InTransactionAsync(async services =>
        {
            var repository = services.GetRequiredService<IWhatsAppConversationRepository>();
            await repository.GetForUpdateAsync(recipient.ContactId, Ct); contactLocked.SetResult();
            await release.Task.WaitAsync(Ct);
            var db = services.GetRequiredService<ApplicationDbContext>(); var outgoing = await db.Set<WhatsAppOutgoingMessage>().SingleAsync(Ct);
            Assert.NotNull(await repository.GetOutgoingAsync(outgoing.Id, Ct)); return true;
        }, Ct);
        await contactLocked.Task.WaitAsync(Ct);
        try
        {
            await using var scope = second.Services.CreateAsyncScope();
            var reservation = await scope.ServiceProvider.GetRequiredService<IWhatsAppChatDispatchService>().ReserveAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
            Assert.Null(reservation.Value);
        }
        finally { release.TrySetResult(); await holder; }
    }
    private static IStructuredRecordActionChannel Channel(IServiceProvider services) => services.GetServices<IStructuredRecordActionChannel>().Single(channel => channel.ChannelId == "phone");
}
