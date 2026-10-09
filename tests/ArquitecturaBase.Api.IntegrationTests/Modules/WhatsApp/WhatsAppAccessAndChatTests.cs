using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAccessAndChatTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Access_and_manual_workers_on_two_hosts_share_the_recipient_lease()
    {
        var phone = TestPhones.Unique();
        using var meta = new HeldMetaHandler();
        await using var manualHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(WhatsAppInfrastructureRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta)));
        await using var accessHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ArquitecturaBase.Infrastructure.Delivery.IMessageSource<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppOutboundMessage>, WhatsAppMessageSource>();
                services.AddHttpClient(WhatsAppInfrastructureRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta);
            }));
        using var client = manualHost.CreateClient();
        var actor = await ReaderAsync(WhatsAppPermissions.Send);
        var bsuid = await WhatsAppTestConversation.ReceiveBusinessAsync(factory, client, phone);
        var contact = await WhatsAppTestConversation.ContactAsync(factory, bsuid);
        using var request = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{contact.Id}/messages",
            JsonContent.Create(new { requestId = Guid.NewGuid(), body = "Respuesta del negocio" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var manual = manualHost.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        try
        {
            await meta.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            await using var scope = accessHost.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
            {
                Assert.True(scope.ServiceProvider.GetRequiredService<WhatsAppSendQueue>().TryEnqueue(new WhatsAppLoginCodeMessage(phone, "es", "135790")));
                return Task.FromResult(Result.Success());
            }, CommitPolicy.OnSuccess, Ct);
            factory.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(200, Ct);
            Assert.Equal(1, meta.Calls);
            Assert.Equal(1, meta.MaximumConcurrent);
        }
        finally { meta.Release.TrySetResult(); }
        await manual.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (meta.Calls < 2)
        {
            factory.Clock.Advance(TimeSpan.FromMilliseconds(250));
            // Redis expiry uses real time. Do not exhaust the coordinator's bounded fake-time retries before its cooldown expires.
            await Task.Delay(250, timeout.Token);
        }
        Assert.Equal(1, meta.MaximumConcurrent);
        Assert.Equal(2, meta.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_business_chat_and_explicit_web_login_remain_independent(bool existingAccount)
    {
        var phone = TestPhones.Unique();
        if (existingAccount)
        {
            await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>()
                .CreateAsync(null, phone, phoneConfirmed: false, "Ana", "es", Ct));
        }

        using var client = factory.CreateClient();
        var bsuid = MetaWebhook.UniqueBsuid();
        var now = MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);
        var payload = MetaWebhook.Build(contacts: [MetaWebhook.Contact(phone.Value[1..], bsuid, "Ana")],
            messages: [MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), phone.Value[1..], bsuid, now, "Hola, quiero entrar")]);
        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(payload) };
        webhook.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        webhook.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(payload));
        using var received = await client.SendAsync(webhook, Ct);
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        await factory.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
        Assert.Empty(factory.WhatsApp.SentTo(phone));
        var contact = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().AsNoTracking()
            .SingleAsync(item => item.UserIdentifier == bsuid, Ct));
        Assert.Equal(WhatsAppConversationMode.Manual, contact.Mode);
        Assert.Null(contact.UserId);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.PhoneNumber == phone.Value && user.PhoneNumberConfirmed, Ct)));

        using var request = await client.PostJsonAsync("/account/login-code/whatsapp", new { number = phone.Value });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var message = Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(factory.WhatsApp.SentTo(phone)));
        Assert.Equal(WhatsAppMessagePurpose.Access, message.Purpose);
        Assert.Equal(WhatsAppAccessEventKind.SignInCode, message.AccessEventKind);
        using var verify = await client.PostJsonAsync("/account/login-code/verify", new { phone = phone.Value, code = message.Code, returnUrl = AuthFlow.AuthorizeReturnUrl });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.True(verify.Headers.Contains("Set-Cookie"));

        // Persist the simulated provider acceptance through the production delivery service, then read the HTTP history.
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IWhatsAppDeliveryService>()
            .RecordSentAsync(message, MetaWebhook.UniqueWaMessageId(), Ct));
        var actor = await ReaderAsync();
        using var history = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contact.Id}/messages", userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var thread = await history.ReadJsonAsync();
        Assert.Equal("Manual", thread.GetProperty("contact").GetProperty("mode").GetString());
        Assert.Equal("Access", thread.GetProperty("contact").GetProperty("previewPurpose").GetString());
        var access = Assert.Single(thread.GetProperty("messages").EnumerateArray(), item => item.GetProperty("purpose").GetString() == "Access");
        Assert.Equal("SignInCode", access.GetProperty("accessEventKind").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, access.GetProperty("body").ValueKind);
        Assert.DoesNotContain(message.Code, thread.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(thread.GetProperty("messages").EnumerateArray(), item => item.GetProperty("body").GetString() == "Hola, quiero entrar" && item.GetProperty("purpose").GetString() == "Business");
    }

    private Task<Guid> ReaderAsync(params string[] additionalPermissions) => factory.InTransactionAsync(async services =>
    {
        var roleName = "access-reader-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, [WhatsAppPermissions.Read, .. additionalPermissions], Ct);
        var users = services.GetRequiredService<IUserRepository>();
        var user = await users.CreateAsync(Email.Create(roleName + "@example.com").Value, null, false, "Operador", "es", Ct);
        await users.SetRolesAsync(user.Id, [roleName], Ct);
        return user.Id;
    });

    private sealed class HeldMetaHandler : HttpMessageHandler
    {
        private int calls;
        private int active;
        public int Calls => Volatile.Read(ref calls);
        public int MaximumConcurrent { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref active);
            MaximumConcurrent = Math.Max(MaximumConcurrent, current);
            var call = Interlocked.Increment(ref calls);
            try
            {
                if (call == 1)
                {
                    Started.TrySetResult();
                    await Release.Task.WaitAsync(cancellationToken);
                }
                return FakeMetaHandler.Success(MetaWebhook.UniqueWaMessageId());
            }
            finally { Interlocked.Decrement(ref active); }
        }
    }
}
