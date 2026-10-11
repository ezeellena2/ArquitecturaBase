using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppFormsEligibilityTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Form_choices_require_chat_read_and_send_and_do_not_infer_a_recipient_link_or_inherit_operator_permissions()
    {
        using var actor = await OperatorAsync(WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formrecords.create");
        var contactId = await factory.InTransactionAsync(services =>
        {
            var contact = WhatsAppContact.Create("54911" + Random.Shared.Next(10000000, 99999999), null, "Form recipient", factory.Clock.GetUtcNow().UtcDateTime);
            services.GetRequiredService<IWhatsAppContactRepository>().Add(contact);
            return Task.FromResult(contact.Id);
        });
        using var choices = await actor.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var empty = await choices.ReadJsonAsync(); Assert.Empty(empty.GetProperty("page").GetProperty("items").EnumerateArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, empty.GetProperty("recipientContextRevision").ValueKind);
        using var thread = await actor.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct);
        var contact = (await thread.ReadJsonAsync()).GetProperty("contact");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, contact.GetProperty("linkedAccount").ValueKind);
        Assert.Equal("NoLinkedAccount", contact.GetProperty("formEligibility").GetProperty("status").GetString());
        using var employee = await OperatorAsync();
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        await factory.InTransactionAsync(async services =>
        {
            var linked = await services.GetRequiredService<IWhatsAppConversationRepository>().GetForUpdateAsync(contactId, Ct);
            linked!.LinkUser(employeeId);
        });
        using var linkedThread = await actor.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct);
        var linkedContact = (await linkedThread.ReadJsonAsync()).GetProperty("contact");
        Assert.Equal(employeeId, linkedContact.GetProperty("linkedAccount").GetProperty("userId").GetGuid());
        Assert.True(linkedContact.GetProperty("linkedAccount").GetProperty("isActive").GetBoolean());
        Assert.Equal("NoEligibleForms", linkedContact.GetProperty("formEligibility").GetProperty("status").GetString());
        foreach (var permissions in new[] { new[] { WhatsAppPermissions.Read }, new[] { WhatsAppPermissions.Send } })
        {
            using var denied = await OperatorAsync(permissions);
            using var response = await denied.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var anonymous = factory.CreateClient();
        using var unauthenticated = await anonymous.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    private async Task<HttpClient> OperatorAsync(params string[] permissions)
    {
        var userId = await factory.InTransactionAsync(async services =>
        {
            var roleName = "form-chat-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, permissions, Ct);
            var user = await services.GetRequiredService<IUserRepository>().CreateAsync(
                Email.Create(roleName + "@example.com").Value, null, false, "Operador", "es", Ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(user.Id, [roleName], Ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return client;
    }
}
