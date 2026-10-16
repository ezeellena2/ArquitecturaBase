using System.Runtime.CompilerServices;
using ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppFormActionScenario : IAsyncDisposable
{
    public WebApplicationFactory<Program> Host { get; }
    public string ConnectionString { get; }
    public IServiceProvider Services => Host.Services;
    public ActionPrivateFiles Files { get; } = new();
    public ActionCloudTransport Cloud { get; } = new();
    public ActionMediaTransport Media { get; } = new();
    private WhatsAppFormActionScenario(ApiFactory factory)
    {
        ConnectionString = factory.NewDatabaseConnectionString("form_action");
        Host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:appdb", ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(Files));
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppCloudClient>(Cloud));
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppMediaUploadClient>(Media));
            });
        });
    }
    public static async Task<WhatsAppFormActionScenario> CreateAsync(ApiFactory factory, CancellationToken ct)
    {
        var scenario = new WhatsAppFormActionScenario(factory);
        await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(ct));
        await scenario.Services.SeedDatabaseAsync(ct);
        return scenario;
    }
    public async Task<T> ScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope(); return await action(scope.ServiceProvider);
    }
    public async Task ScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope(); await action(scope.ServiceProvider);
    }
    public async Task<T> InTransactionAsync<T>(Func<IServiceProvider, Task<T>> action, CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope(); T value = default!;
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            token.ThrowIfCancellationRequested(); value = await action(scope.ServiceProvider); return Result.Success();
        }, CommitPolicy.OnSuccess, ct);
        return value;
    }
    public Task<(Guid UserId, Guid ContactId)> RecipientAsync(DateTime nowUtc, CancellationToken ct) => InTransactionAsync(async services =>
    {
        var phone = PhoneNumber.Create("+" + MetaWebhook.UniqueWaId()).Value;
        var user = await services.GetRequiredService<IUserRepository>().CreateAsync(null, phone, true, "Persona de la visita", "es", ct);
        var contact = WhatsAppContact.Create(phone.Value[1..], MetaWebhook.UniqueBsuid(), "Visita " + Guid.NewGuid().ToString("N"), nowUtc);
        contact.LinkUser(user.Id); services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().Add(contact);
        return (user.Id, contact.Id);
    }, ct);
    public Task<Guid> OperatorAsync(string[] permissions, CancellationToken ct) => InTransactionAsync(async services =>
    {
        var role = "actions-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, permissions, ct);
        var users = services.GetRequiredService<IUserRepository>();
        var user = await users.CreateAsync(Email.Create(role + "@example.com").Value, null, false, "Operador", "es", ct);
        await users.SetRolesAsync(user.Id, [role], ct); return user.Id;
    }, ct);
    public async ValueTask DisposeAsync()
    {
        await ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync());
        await Host.DisposeAsync();
    }
}

internal sealed class ActionPrivateFiles : IPrivateFileStore
{
    public Dictionary<string, byte[]> Content { get; } = [];
    public bool IsAvailable => true;
    public Task<bool> WriteOnceAsync(string key, Stream content, string contentType, CancellationToken ct) => throw new NotSupportedException();
    public Task WriteAsync(string key, Stream content, string contentType, CancellationToken ct) => throw new NotSupportedException();
    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Content.TryGetValue(key, out var content) ? new MemoryStream(content, false) : null);
    public Task DeleteAsync(string key, CancellationToken ct) { Content.Remove(key); return Task.CompletedTask; }
    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, [EnumeratorCancellation] CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); await Task.CompletedTask; yield break; }
}
internal sealed class ActionCloudTransport : IWhatsAppCloudClient
{
    public List<WhatsAppOutboundMessage> Messages { get; } = [];
    public Func<WhatsAppSendResult>? Respond { get; set; }
    public Action? BeforeReturn { get; set; }
    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Messages.Add(message);
        var result = Respond?.Invoke() ?? WhatsAppSendResult.Sent("wamid.action-" + Guid.NewGuid().ToString("N"));
        BeforeReturn?.Invoke(); return Task.FromResult(result);
    }
}
internal sealed class ActionMediaTransport : IWhatsAppMediaUploadClient
{
    public int Calls { get; private set; }
    public byte[]? LastContent { get; private set; }
    public async Task<string?> UploadAsync(WhatsAppOutgoingDocument document, Stream content, CancellationToken cancellationToken)
    {
        Calls++; using var copy = new MemoryStream(); await content.CopyToAsync(copy, cancellationToken); LastContent = copy.ToArray(); return Calls.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
