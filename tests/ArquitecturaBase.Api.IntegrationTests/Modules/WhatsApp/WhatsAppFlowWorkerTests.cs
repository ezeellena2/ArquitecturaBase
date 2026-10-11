using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task Temporary_account_lookup_failure_retries_the_same_flow_step_without_repeating_external_effects()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        await using var host = FormsHost(provider, new CapturingWhatsAppCloudClient(), new CapturingStructuredFormSource(), clock);
        using var actor = await OperatorAsync(host, "formtemplates.publish");
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var operationId = await AcceptFlowAsync(host.Services, actorId);
        var flowOperationId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowOperation>()
            .Where(operation => operation.NeutralOperationId == operationId).Select(operation => operation.Id).SingleAsync(Ct));
        var worker = host.Services.GetRequiredService<WhatsAppFlowBackgroundService>();

        provider.MembershipUnavailable = true;
        await worker.ProcessNextAsync(operationId, Ct);
        var initial = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .Where(binding => binding.OperationId == flowOperationId).ToArrayAsync(Ct));
        Assert.NotEmpty(initial);
        Assert.All(initial, binding => Assert.Equal(WhatsAppFlowPublicationState.Accepted, binding.State));
        Assert.Equal(0, provider.Creates);

        provider.MembershipUnavailable = false;
        clock.Advance(TimeSpan.FromSeconds(16));
        await worker.ProcessNextAsync(operationId, Ct);
        Assert.Equal(1, provider.Creates);
        var created = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .Where(binding => binding.OperationId == flowOperationId && binding.MetaFlowId != null).SingleAsync(Ct));

        provider.MembershipUnavailable = true;
        clock.Advance(TimeSpan.FromSeconds(16));
        await worker.ProcessNextAsync(operationId, Ct);
        var deferred = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .SingleAsync(binding => binding.Id == created.Id, Ct));
        Assert.Equal(WhatsAppFlowPublicationState.Publishing, deferred.State);
        Assert.Equal(created.MetaFlowId, deferred.MetaFlowId);
        Assert.Equal(0, provider.Uploads);

        provider.MembershipUnavailable = false;
        clock.Advance(TimeSpan.FromSeconds(16));
        await worker.ProcessNextAsync(operationId, Ct);
        Assert.Equal(1, provider.Uploads);
        var resumed = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .SingleAsync(binding => binding.Id == created.Id, Ct));
        Assert.Equal(created.MetaFlowId, resumed.MetaFlowId);
    }

    private static async Task<Guid> AcceptFlowAsync(IServiceProvider services, Guid actorId)
    {
        var snapshot = new StructuredFormCheckRequest(1, "es",
            new Dictionary<string, string> { ["es"] = "Formulario", ["en"] = "Form" },
            new Dictionary<string, string> { ["es"] = "Completar", ["en"] = "Submit" },
            [Question()], ["es", "en"]);
        var channel = Assert.Single(services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var checkedForm = (await channel.CheckAsync(snapshot, Ct)).Value!;
        Assert.Empty(checkedForm.Issues);
        var operationId = Guid.NewGuid();
        var request = new StructuredFormPublicationRequest(operationId, Guid.NewGuid(), Guid.NewGuid(), actorId,
            ["formtemplates.publish"], "worker-test-fingerprint", checkedForm.ProfileRevision,
            checkedForm.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(), snapshot);
        Assert.True((await channel.AcceptAsync(request, Ct)).IsSuccess);
        return operationId;
    }

    private static StructuredFormQuestion Question()
    {
        var fieldId = Guid.NewGuid();
        return new StructuredFormQuestion(fieldId, "field_" + fieldId.ToString("N"), "shortText",
            new Dictionary<string, string> { ["es"] = "Nombre", ["en"] = "Name" }, null, false, 100, null);
    }
}
