using ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed record WhatsAppAssistantContact(Guid RecipientId, Guid ContactId, Guid SourceMessageId);

internal sealed class WhatsAppAssistantScenario : IAsyncDisposable
{
    public WebApplicationFactory<Program> Host { get; }
    public FakeTimeProvider Clock { get; }
    public WhatsAppAssistantOperationsDouble Operations { get; } = new();
    public WhatsAppAssistantFormSource Forms { get; } = new();
    public ActionCloudTransport Cloud { get; } = new();
    public Guid FirstOperator { get; private set; }
    public Guid SecondOperator { get; private set; }
    public Guid RestrictedOperator { get; private set; }
    public Guid ReadOnlyOperator { get; private set; }
    private WhatsAppAssistantScenario(ApiFactory factory)
    {
        Clock = new(factory.Clock.GetUtcNow());
        var flowProvider = new CapturingFlowProvider(Clock);
        var connection = factory.NewDatabaseConnectionString("assistant_http");
        Host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection)
            .UseSetting("WhatsApp:WabaId", "222").ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IAssistantOperations>(Operations));
                services.Replace(ServiceDescriptor.Singleton<IStructuredFormSource>(Forms));
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppFlowProvider>(flowProvider));
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppCloudClient>(Cloud));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                // Redis lease TTLs use real time, independently of the functional clock.
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppDispatchCoordinator>(provider =>
                    new WhatsAppDispatchCoordinator(provider.GetRequiredService<RedisCache>(), TimeProvider.System)));
            }));
    }
    public static async Task<WhatsAppAssistantScenario> CreateAsync(ApiFactory factory, CancellationToken ct)
    {
        var scenario = new WhatsAppAssistantScenario(factory);
        await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(ct));
        await scenario.Host.Services.SeedDatabaseAsync(ct);
        scenario.FirstOperator = await scenario.OperatorAsync([WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formrecords.read", "formtemplates.publish"], ct);
        scenario.SecondOperator = await scenario.OperatorAsync([WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formrecords.read"], ct);
        scenario.RestrictedOperator = await scenario.OperatorAsync([WhatsAppPermissions.Read, WhatsAppPermissions.Send], ct);
        scenario.ReadOnlyOperator = await scenario.OperatorAsync([WhatsAppPermissions.Read], ct);
        await scenario.PublishAsync(ct); return scenario;
    }
    public HttpClient Client(Guid? actor = null)
    {
        var client = Host.CreateClient();
        if (actor is {} id) client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, id.ToString());
        return client;
    }
    public async Task<T> ScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    { await using var scope = Host.Services.CreateAsyncScope(); return await action(scope.ServiceProvider); }
    public async Task ScopeAsync(Func<IServiceProvider, Task> action)
    { await using var scope = Host.Services.CreateAsyncScope(); await action(scope.ServiceProvider); }
    public async Task<T> InTransactionAsync<T>(Func<IServiceProvider, Task<T>> action, CancellationToken ct)
    {
        await using var scope = Host.Services.CreateAsyncScope(); T value = default!;
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        { token.ThrowIfCancellationRequested(); value = await action(scope.ServiceProvider); return Result.Success(); }, CommitPolicy.OnSuccess, ct);
        Assert.True(result.IsSuccess); return value;
    }
    public Task<WhatsAppAssistantContact> ContactAsync(CancellationToken ct) => InTransactionAsync(async services =>
    {
        var phone = PhoneNumber.Create("+" + MetaWebhook.UniqueWaId()).Value;
        var user = await services.GetRequiredService<IUserRepository>().CreateAsync(null, phone, true, "Persona de la visita", "es", ct);
        var role = "assistant-recipient-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, ["formrecords.create"], ct);
        await services.GetRequiredService<IUserRepository>().SetRolesAsync(user.Id, [role], ct);
        var now = Clock.GetUtcNow().UtcDateTime;
        var contact = WhatsAppContact.Create(phone.Value[1..], MetaWebhook.UniqueBsuid(), "Persona ficticia", now); contact.LinkUser(user.Id);
        var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text,
            "Visité Sucursal Norte.", null, now); message.Classify(WhatsAppMessagePurpose.Business);
        message.SetInboundSequence(contact.RegisterInboundMessage()); message.MarkProcessed(now);
        services.GetRequiredService<ApplicationDbContext>().AddRange(contact, message);
        return new WhatsAppAssistantContact(user.Id, contact.Id, message.Id);
    }, ct);
    public async Task CompleteAsync(WhatsAppAssistantContact contact, Guid requestId, string state, CancellationToken ct)
    {
        var work = await ScopeAsync(async services => (await services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId, ct)).Value);
        Assert.NotNull(work); Assert.Equal(requestId, work.RequestId);
        var proposal = state switch
        {
            "ready" => Forms.Proposal(contact.RecipientId),
            "no-reply" => new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), new("whatsapp.no-reply", 1), new WhatsAppAssistantNoReplyPayload("not_form")),
            _ => null
        };
        var outcome = new AssistantOperationResponse(requestId, state == "blocked" ? "failed" : "completed", proposal,
            new("http-fixture", 1, "http-fixture", 1, work.Operation.OccurredAtUtc, work.Operation.Context.ChannelId), null);
        Operations.Complete(outcome);
        var result = await ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantDispatchService>().CompleteAsync(contact.ContactId, requestId, Result.Success(outcome), ct));
        Assert.True(result.IsSuccess);
    }
    public async Task AddNewerSourceAsync(WhatsAppAssistantContact row, CancellationToken ct)
    {
        await InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>(); var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == row.ContactId, ct);
            var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, "Un pedido posterior.", null, Clock.GetUtcNow().UtcDateTime);
            message.Classify(WhatsAppMessagePurpose.Business); message.SetInboundSequence(contact.RegisterInboundMessage()); message.MarkProcessed(Clock.GetUtcNow().UtcDateTime); db.Add(message); return true;
        }, ct);
    }
    private Task<Guid> OperatorAsync(string[] permissions, CancellationToken ct) => InTransactionAsync(async services =>
    {
        var role = "assistant-http-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, permissions, ct);
        var users = services.GetRequiredService<IUserRepository>();
        var user = await users.CreateAsync(Email.Create(role + "@example.com").Value, null, false, "Operador ficticio", "es", ct);
        await users.SetRolesAsync(user.Id, [role], ct); return user.Id;
    }, ct);
    private async Task PublishAsync(CancellationToken ct)
    {
        var channel = Assert.Single(Host.Services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var check = (await channel.CheckAsync(Forms.Snapshot, ct)).Value; Assert.Empty(check.Issues);
        var operationId = Guid.NewGuid();
        var request = new StructuredFormPublicationRequest(operationId, Forms.Form.DefinitionId, Forms.Form.VersionId, FirstOperator,
            ["formtemplates.publish"], "http-assistant-publication", check.ProfileRevision,
            check.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(), Forms.Snapshot);
        Assert.True((await channel.AcceptAsync(request, ct)).IsSuccess);
        for (var step = 0; step < 12; step++)
        { await Host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, ct); Clock.Advance(TimeSpan.FromSeconds(16)); }
        var bindings = await ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppFlowBinding>().AsNoTracking().Where(item => item.VersionId == Forms.Form.VersionId).ToListAsync(ct));
        Assert.All(bindings, item => Assert.Equal(WhatsAppFlowPublicationState.Published, item.State)); Assert.Equal(2, bindings.Count);
    }
    public async ValueTask DisposeAsync()
    { await ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync()); await Host.DisposeAsync(); }
}
