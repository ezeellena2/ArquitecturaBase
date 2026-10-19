using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Two_hosts_capturing_distinct_sessions_allocate_distinct_consecutive_numbers()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var field = Field("shortText", required: true);
        var fields = new[] { field };
        var definition = BusinessFormDefinition.Create();
        var version = BusinessFormVersion.Create(definition.Id, 1, "concurrent-test");
        var prefix = "C" + Guid.NewGuid().ToString("N")[..8];
        Assert.True(definition.SetDraft(1, "Concurrente", "{\"es\":\"Concurrente\",\"en\":\"Concurrent\"}", prefix, "es",
            "{\"es\":\"Enviar\",\"en\":\"Submit\"}", JsonSerializer.Serialize(new FormQuestionSchema(1, fields), SerializerOptions), 1, creating: true).IsSuccess);
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), SerializerOptions));
        version.SealPresentation("{\"es\":\"Concurrente\",\"en\":\"Concurrent\"}", "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", 1);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, actorId, ["es", "en"]));
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        await using var firstHost = factory.WithWebHostBuilder(_ => { });
        await using var secondHost = factory.WithWebHostBuilder(_ => { });
        using var firstScope = firstHost.Services.CreateScope();
        using var secondScope = secondHost.Services.CreateScope();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        async Task<StructuredRecordCaptureResponse> CaptureAsync(IServiceProvider services, string value)
        {
            var sink = services.GetRequiredService<IStructuredRecordSink>();
            if (Interlocked.Increment(ref arrivals) == 2) ready.SetResult();
            await start.Task.WaitAsync(Ct);
            return await sink.CaptureAsync(new(Guid.NewGuid(), definition.Id, version.Id, actorId,
                factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string> { [field.Key] = value }), Ct);
        }
        var first = CaptureAsync(firstScope.ServiceProvider, "first");
        var second = CaptureAsync(secondScope.ServiceProvider, "second");
        await ready.Task.WaitAsync(Ct);
        start.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.All(results, result => Assert.Equal("Completed", result.State));
        Assert.Equal(new long?[] { 1, 2 }, results.Select(result => result.Number).Order().ToArray());
        Assert.Equal(2, results.Select(result => result.RecordId).Distinct().Count());
        var persisted = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>()
            .Where(record => record.DefinitionId == definition.Id).OrderBy(record => record.Number).ToArrayAsync(Ct));
        Assert.Equal(new long[] { 1, 2 }, persisted.Select(record => record.Number));
        Assert.Equal(2, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>()
            .Where(item => item.Id == definition.Id).Select(item => item.LastRecordNumber).SingleAsync(Ct)));
    }
}
