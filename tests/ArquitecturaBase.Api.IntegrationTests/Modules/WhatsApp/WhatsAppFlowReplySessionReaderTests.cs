using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task Reply_failures_persist_their_own_terminal_rejection_across_scopes()
    {
        using var employee = await OperatorAsync(factory, "formrecords.create");
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var contactId = await CreateLinkedContactAsync(employeeId, factory.Clock);
        var waMessageId = "wamid.failure-" + Guid.NewGuid().ToString("N");
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<WhatsAppMessage>().Add(WhatsAppMessage.Inbound(contactId, waMessageId,
                WhatsAppMessageKind.FlowReply, null, null, factory.Clock.GetUtcNow().UtcDateTime, [1]));
            await db.SaveChangesAsync(Ct);
        });
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var scope = host.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyStateService>()
                .RegisterFailureAsync(waMessageId, null, Ct);
            Assert.True(result.IsSuccess);
            await factory.ExecuteDbContextAsync(async db =>
            {
                var message = await db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(item => item.WaMessageId == waMessageId, Ct);
                Assert.Equal(attempt, message.FlowReplyFailureCount);
                Assert.Equal(attempt == 5, message.ProcessedAtUtc.HasValue);
                if (attempt == 5)
                {
                    Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
                    Assert.Equal(FlowErrors.CaptureFailed.Code, message.FlowCaptureErrorCode);
                    Assert.Null(message.FlowRecordId);
                }
            });
            clock.Advance(TimeSpan.FromMinutes(4));
        }
        using var finalScope = factory.Services.CreateScope();
        var ownMessage = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(message => message.WaMessageId == waMessageId, Ct));
        Assert.NotNull(ownMessage.ProcessedAtUtc);
        Assert.Equal(WhatsAppFlowCaptureState.Rejected, ownMessage.FlowCaptureState);
        var thread = await finalScope.ServiceProvider.GetRequiredService<IWhatsAppChatReader>().FindThreadAsync(new(contactId), employeeId, Ct);
        var rejected = Assert.Single(thread!.Messages, item => item.Kind == WhatsAppMessageKind.FlowReply);
        Assert.Equal("Rejected", rejected.FormReply!.State);
        Assert.Equal(FlowErrors.CaptureFailed.Code, rejected.FormReply.RejectionCode);
        await finalScope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyStateService>().RegisterFailureAsync(waMessageId, null, Ct);
        await factory.ExecuteDbContextAsync(async db => Assert.Equal(5,
            (await db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(item => item.WaMessageId == waMessageId, Ct)).FlowReplyFailureCount));
    }

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
