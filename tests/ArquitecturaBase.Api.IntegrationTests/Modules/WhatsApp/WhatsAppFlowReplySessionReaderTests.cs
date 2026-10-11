using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Theory]
    [InlineData("Current", true)]
    [InlineData("Unlinked", false)]
    [InlineData("Relinked", false)]
    [InlineData("Inactive", false)]
    [InlineData("OtherContact", false)]
    [InlineData("Expired", true)]
    public async Task Reply_session_lookup_preserves_sealed_identity_and_reports_current_recipient_validity(string scenario, bool current)
    {
        using var employee = await OperatorAsync(factory, "formrecords.create");
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var versionId = Guid.NewGuid(); var definitionId = Guid.NewGuid();
        var operation = WhatsAppFlowOperation.Accept(Guid.NewGuid(), definitionId, versionId, employeeId);
        operation.SealManifest([1]);
        var binding = WhatsAppFlowBinding.Create(operation.Id, versionId, "es", "session-reader-test", [2]);
        var contactId = await CreateLinkedContactAsync(employeeId, factory.Clock);
        var tokenHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var expiresAtUtc = now.AddHours(scenario == "Expired" ? -1 : 24);
        var session = WhatsAppFlowSession.Create(contactId, 1, employeeId, employeeId, definitionId, versionId,
            1, binding.Id, "es", "{\"es\":\"Prueba\"}", "READ", tokenHash, new string('a', 64), expiresAtUtc);
        session.SealPayload([3]);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<WhatsAppFlowOperation>().Add(operation);
            db.Set<WhatsAppFlowBinding>().Add(binding);
            db.Set<WhatsAppFlowSession>().Add(session);
            await db.SaveChangesAsync(Ct);
        });
        if (scenario is "Unlinked" or "Relinked" or "Inactive")
            await factory.ExecuteDbContextAsync(async db =>
            {
                var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
                if (scenario == "Inactive") (await db.Users.SingleAsync(user => user.Id == employeeId, Ct)).SetActive(false);
                else
                {
                    contact.UnlinkUser();
                    if (scenario == "Relinked") contact.LinkUser(employeeId);
                }
                await db.SaveChangesAsync(Ct);
            });
        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowSessionReader>();
        var row = await reader.FindReplySessionAsync(scenario == "OtherContact" ? Guid.NewGuid() : contactId, tokenHash, Ct);
        if (scenario == "OtherContact") { Assert.Null(row); return; }
        Assert.NotNull(row);
        Assert.Equal(session.Id, row.SessionId);
        Assert.Equal(employeeId, row.UserId);
        Assert.Equal(definitionId, row.DefinitionId);
        Assert.Equal(versionId, row.VersionId);
        Assert.Equal(current, row.IsCurrentActiveRecipient);
        Assert.Equal(expiresAtUtc, row.ExpiresAtUtc);
        Assert.Equal(new byte[] { 2 }, row.ProtectedProjection);
        Assert.Equal(new byte[] { 3 }, row.ProtectedPayload);
    }
}
