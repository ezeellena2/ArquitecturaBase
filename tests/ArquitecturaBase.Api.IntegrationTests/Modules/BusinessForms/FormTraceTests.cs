using System.Collections.Concurrent;
using System.Diagnostics;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// F18: an action run keeps the trace context where it was created and its worker continues it, so a response can be followed from the
// capture to the notice; without a trace in progress the run keeps none and the worker starts its own.
public sealed partial class FormDraftEndpointsTests
{
    private static readonly ActivitySource TestSource = new("ArquitecturaBase.Tests.FormTrace");

    [Fact]
    public async Task The_worker_continues_the_trace_where_the_action_run_was_created()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);
        string? parent;
        ActivityTraceId trace;
        Guid runId;
        using (var span = TestSource.StartActivity("capture"))
        {
            Assert.NotNull(span);
            trace = span.TraceId;
            var (created, worker) = await CaptureNoticeRunAsync();
            runId = created;
            parent = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().Where(run => run.Id == runId)
                .Select(run => run.TraceParent).SingleAsync(Ct));
            Assert.StartsWith($"00-{trace.ToHexString()}-", parent, StringComparison.Ordinal);
            await worker(runId);
        }

        var dispatch = Assert.Single(stopped, activity => activity.OperationName == "forms.action");
        Assert.Equal(trace, dispatch.TraceId);
        Assert.Equal(parent!.Split('-')[2], dispatch.ParentSpanId.ToHexString());
        Assert.Equal("NotifyAuthor", dispatch.GetTagItem("forms.action.kind"));
        Assert.Equal(runId.ToString(), dispatch.GetTagItem("forms.run.id"));
    }

    [Fact]
    public async Task An_action_run_created_without_a_trace_keeps_none_and_its_worker_still_runs()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var (runId, worker) = await CaptureNoticeRunAsync();
            Assert.Null(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().Where(run => run.Id == runId)
                .Select(run => run.TraceParent).SingleAsync(Ct)));
            await worker(runId);
            var dispatch = Assert.Single(stopped, activity => activity.OperationName == "forms.action");
            Assert.Equal(default, dispatch.ParentSpanId);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    private static ActivityListener Listen(ConcurrentBag<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is FormsTelemetry.SourceName or "ArquitecturaBase.Tests.FormTrace",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    // A response captured by a form whose rule notifies its author, and the worker that executes the run it creates.
    private async Task<(Guid RunId, Func<Guid, Task> Process)> CaptureNoticeRunAsync()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsCreate]);
        var action = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string, string> { { "es", "Recibimos {número}" } }, false);
        var rules = new[] { new FormRule(Guid.NewGuid(), "Received", [], [action]) };
        var seeded = await SeedSheetAsync(actor, 0);
        var author = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var versionId = await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == seeded.Id, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == definition.ActiveVersionId, Ct);
            var ruleSet = BusinessFormRuleSet.Create(definition.Id, 1, "es", System.Text.Json.JsonSerializer.Serialize(rules, SerializerOptions), false, version.SchemaJson);
            db.Set<BusinessFormRuleSet>().Add(ruleSet);
            definition.SetActiveRuleSetId(ruleSet.Id);
            await db.SaveChangesAsync(Ct);
            return version.Id;
        });
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), seeded.Id, versionId, author, factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string>());
        var captured = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IBusinessFormRecordCaptureService>().CaptureAsync(request, Ct));
        var channel = new CapturingFormActionChannel();
        channel.Reachable.Add(author);
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredRecordActionChannel>();
            services.AddSingleton<IStructuredRecordActionChannel>(channel);
        }));
        Func<Guid, Task> process = async id =>
        {
            using var worker = new FormActionBackgroundService(host.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
                Options.Create(new FormActionWorkerOptions { ProcessInBackground = false }), NullLogger<FormActionBackgroundService>.Instance);
            await worker.ProcessNextAsync(id, Ct);
            Assert.Single(channel.Messages);
            await host.DisposeAsync();
        };
        var runId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().Where(run => run.RecordId == captured.RecordId)
            .Select(run => run.Id).SingleAsync(Ct));
        return (runId, process);
    }
}
