using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Lost_completion_after_channel_acceptance_reconciles_when_publication_permission_is_revoked()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var channel = new RetryingStructuredFormChannel { FailuresBeforeAccept = 0 };
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannel>();
            services.AddSingleton<IStructuredFormChannel>(channel);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actorId.ToString());
        var saved = await CreateAsync(client, Payload("Lost publication completion"));
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var attempt = await PublicationAttemptAsync(client, definitionId, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var operationId = (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid();

        await using (var reservationScope = host.Services.CreateAsyncScope())
        {
            var result = await reservationScope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>().ReserveAsync(operationId, Ct);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            await using var dispatchScope = host.Services.CreateAsyncScope();
            var dispatched = await dispatchScope.ServiceProvider.GetRequiredService<IFormPublicationChannelDispatchService>().DispatchAsync(result.Value, Ct);
            Assert.True(dispatched.IsSuccess);
            Assert.Equal("Published", Assert.Single(dispatched.Value.Progress).Response.State);
            // Deliberately omit CompleteAsync: the process died after the channel confirmed its durable acceptance.
        }
        Assert.Equal(1, channel.AcceptCalls);
        var unfinished = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().SingleAsync(row => row.Id == operationId, Ct));
        Assert.Equal(FormPublicationState.Preparing, unfinished.State);
        Assert.Equal(1, unfinished.Fence);
        var role = await factory.ExecuteDbContextAsync(db => db.UserRoles.Where(link => link.UserId == actorId)
            .Join(db.Roles, link => link.RoleId, row => row.Id, (link, row) => new { row.Id, row.Name }).SingleAsync(Ct));
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IRoleRepository>().UpdateAsync(role.Id, role.Name!, null, [BusinessFormsPermissions.Read], Ct);
            return true;
        });
        clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));

        await host.Services.GetRequiredService<FormPublicationBackgroundService>().ProcessNextAsync(operationId, Ct);

        var reconciled = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().SingleAsync(row => row.Id == operationId, Ct));
        Assert.Equal(FormPublicationState.Uncertain, reconciled.State);
        Assert.True(reconciled.IsOpen);
        Assert.Equal(1, channel.AcceptCalls);
        var definition = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().SingleAsync(row => row.Id == definitionId, Ct));
        Assert.Null(definition.ActiveVersionId);
    }
}
