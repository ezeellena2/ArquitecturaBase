using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFlowReconciliationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unreadable_flow_does_not_block_the_next_operation_or_erase_an_inflight_effect(bool projectionWithInFlightEffect)
    {
        await using var isolated = new ApiFactory();
        await isolated.InitializeAsync();
        var clock = new FakeTimeProvider(isolated.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        await using var host = isolated.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:WabaId", "222")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IWhatsAppFlowProvider>();
                services.AddSingleton<IWhatsAppFlowProvider>(provider);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.PostConfigure<WhatsAppOptions>(options => options.ProcessFlowsInBackground = false);
            }));
        var actorId = await isolated.InTransactionAsync(async services =>
        {
            var name = "flow-corruption-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(name, null, ["formtemplates.publish"], Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(name + "@example.com").Value, null, false, "Operador", "es", Ct);
            await users.SetRolesAsync(user.Id, [name], Ct);
            return user.Id;
        });
        var firstId = await AcceptFlowAsync(host.Services, actorId);
        clock.Advance(TimeSpan.FromSeconds(1));
        var secondId = await AcceptFlowAsync(host.Services, actorId);
        Guid? inFlightBindingId = null;
        if (projectionWithInFlightEffect)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var dispatch = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowDispatchService>();
            var reserved = await dispatch.ReserveAsync(firstId, Ct);
            Assert.True(reserved.IsSuccess);
            Assert.NotNull(reserved.Value);
            Assert.True((await dispatch.MarkEffectStartedAsync(reserved.Value, Ct)).IsSuccess);
            inFlightBindingId = reserved.Value.BindingId;
            clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        }
        await isolated.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var operation = await db.Set<WhatsAppFlowOperation>().SingleAsync(row => row.NeutralOperationId == firstId, Ct);
            if (inFlightBindingId is { } bindingId)
            {
                var binding = await db.Set<WhatsAppFlowBinding>().SingleAsync(row => row.Id == bindingId, Ct);
                db.Entry(binding).Property(row => row.ProtectedProjection).CurrentValue = [1, 2, 3];
            }
            else db.Entry(operation).Property(row => row.ProtectedManifest).CurrentValue = [1, 2, 3];
            return true;
        });

        var worker = host.Services.GetRequiredService<WhatsAppFlowBackgroundService>();
        await worker.ProcessNextAsync(null, Ct);

        Assert.Equal(1, provider.Creates);
        var corrupt = await isolated.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowOperation>().SingleAsync(row => row.NeutralOperationId == firstId, Ct));
        Assert.False(corrupt.Completed);
        Assert.True(corrupt.NextCheckAtUtc > clock.GetUtcNow().UtcDateTime);
        var healthyOperationId = await isolated.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowOperation>()
            .Where(row => row.NeutralOperationId == secondId).Select(row => row.Id).SingleAsync(Ct));
        Assert.Equal(1, await isolated.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .CountAsync(row => row.OperationId == healthyOperationId && row.MetaFlowId != null, Ct)));
        if (inFlightBindingId is { } effectBindingId)
        {
            var binding = await isolated.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>().SingleAsync(row => row.Id == effectBindingId, Ct));
            Assert.True(binding.InFlight);
            Assert.NotEqual(WhatsAppFlowPublicationState.Failed, binding.State);
        }
        await worker.ProcessNextAsync(null, Ct);
        Assert.Equal(1, provider.Creates);
    }
}
