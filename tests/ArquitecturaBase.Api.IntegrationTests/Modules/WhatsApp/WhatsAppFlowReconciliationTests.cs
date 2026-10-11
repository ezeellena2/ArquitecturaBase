using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed partial class WhatsAppFlowReconciliationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Account_lookup_failure_during_reconciliation_does_not_repeat_an_uncertain_publish()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock) { PublishUncertainOnce = true };
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:WabaId", "222")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IWhatsAppFlowProvider>();
                services.AddSingleton<IWhatsAppFlowProvider>(provider);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            }));
        using var actor = await OperatorAsync(host, "formtemplates.publish");
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var operationId = await AcceptFlowAsync(host.Services, actorId);
        var flowOperationId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowOperation>()
            .Where(operation => operation.NeutralOperationId == operationId).Select(operation => operation.Id).SingleAsync(Ct));
        var worker = host.Services.GetRequiredService<WhatsAppFlowBackgroundService>();
        WhatsAppFlowBinding? binding = null;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await worker.ProcessNextAsync(operationId, Ct);
            binding = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
                .Where(value => value.OperationId == flowOperationId && value.State == WhatsAppFlowPublicationState.Uncertain)
                .SingleOrDefaultAsync(Ct));
            if (binding?.State == WhatsAppFlowPublicationState.Uncertain) break;
            clock.Advance(TimeSpan.FromSeconds(16));
        }

        var targetBinding = Assert.IsType<WhatsAppFlowBinding>(binding);
        Assert.Equal(WhatsAppFlowPublicationState.Uncertain, targetBinding.State);
        Assert.Equal(WhatsAppFlowStep.Publish, targetBinding.Step);
        Assert.NotNull(targetBinding.MetaFlowId);
        Assert.Equal(1, provider.Creates);
        Assert.Equal(1, provider.Uploads);
        Assert.Equal(1, provider.Publishes);
        var metaFlowId = targetBinding.MetaFlowId;

        provider.MembershipUnavailable = true;
        clock.Advance(TimeSpan.FromSeconds(31));
        await worker.ProcessNextAsync(operationId, Ct);
        targetBinding = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .SingleAsync(value => value.Id == targetBinding.Id, Ct));
        Assert.Equal(WhatsAppFlowPublicationState.Uncertain, targetBinding.State);
        Assert.Equal(WhatsAppFlowStep.Publish, targetBinding.Step);
        Assert.Equal(metaFlowId, targetBinding.MetaFlowId);
        Assert.Equal((1, 1, 1), (provider.Creates, provider.Uploads, provider.Publishes));

        provider.MembershipUnavailable = false;
        for (var retry = 0; retry < 10 && targetBinding.State != WhatsAppFlowPublicationState.Published; retry++)
        {
            clock.Advance(TimeSpan.FromMinutes(2));
            await worker.ProcessNextAsync(operationId, Ct);
            targetBinding = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
                .SingleAsync(value => value.Id == targetBinding.Id, Ct));
            Assert.Equal((1, 1, 1), (provider.Creates, provider.Uploads, provider.Publishes));
        }
        Assert.Equal(WhatsAppFlowPublicationState.Published, targetBinding.State);
        Assert.Equal(metaFlowId, targetBinding.MetaFlowId);
        Assert.Equal((1, 1, 1), (provider.Creates, provider.Uploads, provider.Publishes));
    }

    private static async Task<Guid> AcceptFlowAsync(IServiceProvider services, Guid actorId)
    {
        using var scope = services.CreateScope();
        var channel = Assert.Single(scope.ServiceProvider.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var fieldId = Guid.CreateVersion7();
        var snapshot = new StructuredFormCheckRequest(1, "es",
            new Dictionary<string, string> { ["es"] = "Formulario", ["en"] = "Form" },
            new Dictionary<string, string> { ["es"] = "Completar", ["en"] = "Submit" },
            [new(fieldId, "field_" + fieldId.ToString("N"), "shortText",
            new Dictionary<string, string> { ["es"] = "Nombre", ["en"] = "Name" }, null, false, 100, null)], ["es", "en"]);
        var checkedForm = (await channel.CheckAsync(snapshot, Ct)).Value!;
        Assert.Empty(checkedForm.Issues);
        var operationId = Guid.NewGuid();
        var request = new StructuredFormPublicationRequest(operationId, Guid.NewGuid(), Guid.NewGuid(), actorId,
            ["formtemplates.publish"], "worker-test-fingerprint", checkedForm.ProfileRevision,
            checkedForm.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(), snapshot);
        Assert.True((await channel.AcceptAsync(request, Ct)).IsSuccess);
        return operationId;
    }

    private async Task<HttpClient> OperatorAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,
        params string[] permissions)
    {
        var userId = await factory.InTransactionAsync(async services =>
        {
            var roleName = "flow-reconcile-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, permissions, Ct);
            var user = await services.GetRequiredService<IUserRepository>().CreateAsync(
                Email.Create(roleName + "@example.com").Value, null, false, "Operador", "es", Ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(user.Id, [roleName], Ct);
            return user.Id;
        });
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return client;
    }
}
