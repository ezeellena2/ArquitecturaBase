using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Task 8 composed with the inbox of the forms module, through the neutral port and its HTTP routes (decision 10): a file that a person who
// builds forms sends with a request and that cannot be opened goes to «Documentos recibidos» discarded, a PDF with the inbox's own copy and
// anything else without its file. Meta's download and the private storage are the probe's; the owner's reader and the inbox are the module's.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppReceivedDocumentInboxTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_file_that_cannot_be_opened_reaches_the_real_inbox_discarded_with_its_copy_or_without_its_file()
    {
        var probe = new WhatsAppDocumentProbe();
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(probe));
            services.Replace(ServiceDescriptor.Singleton<IWhatsAppMediaClient>(probe));
        }));
        host.Operations.AllowSubmit = true;
        var person = await host.ContactAsync(Ct, automatic: true);
        await RoleAsync(host, person.RecipientId, "formtemplates.read", "formtemplates.manage", "formrecords.create");
        var reader = await ReaderAsync(host);
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nnot really a document\n");
        var note = Encoding.ASCII.GetBytes("Una nota que no es un documento.");

        probe.Bytes = pdf;
        await ReceiveAsync(host, person, probe, "comprobante.pdf", "application/pdf");
        await ProcessAsync(host);
        probe.Bytes = note;
        await ReceiveAsync(host, person, probe, "nota.txt", "text/plain");
        await ProcessAsync(host);

        using var client = host.Client(reader);
        using var search = await client.PostAsJsonAsync("/api/forms/documents/search", new { senderUserId = person.RecipientId, pageSize = 10 }, Ct);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var items = (await search.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("file").GetProperty("name").GetString()!);
        Assert.Equal(["comprobante.pdf", "nota.txt"], items.Keys.Order(StringComparer.Ordinal));
        Assert.All(items.Values, item => Assert.Equal(("discarded", "unreadable", "whatsapp"),
            (item.GetProperty("state").GetString(), item.GetProperty("reasonCode").GetString(), item.GetProperty("channelId").GetString())));
        Assert.True(items["comprobante.pdf"].GetProperty("file").GetProperty("stored").GetBoolean());
        Assert.Equal("{\"name\":\"nota.txt\",\"mediaType\":\"text/plain\",\"size\":" + note.Length + ",\"pages\":null,\"stored\":false}",
            items["nota.txt"].GetProperty("file").GetRawText());

        using var copy = await client.GetAsync($"/api/forms/documents/{items["comprobante.pdf"].GetProperty("id").GetGuid()}/file", Ct);
        Assert.Equal(HttpStatusCode.OK, copy.StatusCode);
        Assert.Equal(pdf, await copy.Content.ReadAsByteArrayAsync(Ct));
        using var withoutFile = await client.GetAsync($"/api/forms/documents/{items["nota.txt"].GetProperty("id").GetGuid()}/file", Ct);
        Assert.Equal(HttpStatusCode.NotFound, withoutFile.StatusCode);
        Assert.Equal("Forms.ReceivedDocument.FileNotStored", (await withoutFile.ReadJsonAsync()).GetProperty("code").GetString());
        // Each one was answered once in the person's chat.
        var replies = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(row => row.ContactId == person.ContactId).CountAsync(Ct));
        Assert.Equal(2, replies);
    }

    private static async Task ReceiveAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, WhatsAppDocumentProbe probe, string fileName,
        string type)
    {
        var contact = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .SingleAsync(row => row.Id == person.ContactId, Ct));
        var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(contact.WaId, contact.UserIdentifier, contact.ProfileName)],
            messages: [MetaWebhook.Document(MetaWebhook.UniqueWaMessageId(), contact.WaId, contact.UserIdentifier, host.Clock.GetUtcNow().UtcDateTime, fileName,
                type, "Cargalo en pagos", sha256: Convert.ToBase64String(SHA256.HashData(probe.Bytes)))]);
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task ProcessAsync(WhatsAppAssistantScenario host) =>
        host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

    // A role of its own with the permissions, as an administrator would give it.
    private static Task<bool> RoleAsync(WhatsAppAssistantScenario host, Guid userId, params string[] permissions) => host.InTransactionAsync(async services =>
    {
        var role = "document-inbox-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, permissions, Ct);
        await services.GetRequiredService<IUserRepository>().SetRolesAsync(userId, [role], Ct);
        return true;
    }, Ct);

    // Someone who sees «Documentos recibidos».
    private static Task<Guid> ReaderAsync(WhatsAppAssistantScenario host) => host.InTransactionAsync(async services =>
    {
        var role = "document-reader-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, ["formdocuments.read"], Ct);
        var users = services.GetRequiredService<IUserRepository>();
        var user = await users.CreateAsync(Email.Create(role + "@example.com").Value, null, false, "Revisión ficticia", "es", Ct);
        await users.SetRolesAsync(user.Id, [role], Ct);
        return user.Id;
    }, Ct);
}
