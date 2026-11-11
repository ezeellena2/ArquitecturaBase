using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// The «IssueDocument» action with PostgreSQL and the worker run by hand: it is queued once, asked again on the schedule of its
// issuer, keeps the result in the response and waits for a person with the permission of its issuer when it is in review.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task An_issuance_is_queued_once_asked_again_when_its_issuer_says_and_its_result_stays_in_the_response()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage]);
        var issuer = new TestRecordIssuer();
        await using var host = IssuerHost(issuer);
        var (recordId, runId) = await SeedIssueRunAsync(manager);
        var worker = host.Services.GetRequiredService<FormActionBackgroundService>();
        var later = factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(10);

        issuer.Next = new(StructuredIssueState.Pending, WaitingForProvider: true, NextCheckAtUtc: later);
        await worker.ProcessNextAsync(runId, Ct);
        var waiting = await RunAsync(runId);
        Assert.Equal((FormActionRunState.Running, later), (waiting.State, waiting.NextCheckAtUtc));
        Assert.Equal(runId, Assert.Single(issuer.Requests).RunId);

        await worker.ProcessNextAsync(runId, Ct);
        factory.Clock.Advance(TimeSpan.FromMinutes(10));
        issuer.Current = new(StructuredIssueState.Issued, Result: new StructuredRecordResult(TestRecordIssuer.Key,
            new Dictionary<string, string> { ["es"] = "Comprobante de prueba", ["en"] = "Test voucher" },
            [new("number", new Dictionary<string, string> { ["es"] = "Número", ["en"] = "Number" }, StructuredRecordResultField.Text, "00004-00000012")]),
            Document: new StructuredRecordActionDocument($"test-issue/{runId:N}.pdf", "Factura C 00004-00000012.pdf", "application/pdf", 2048, new string('a', 64)));
        await worker.ProcessNextAsync(runId, Ct);

        Assert.Single(issuer.Requests);
        Assert.Equal(FormActionRunState.Completed, (await RunAsync(runId)).State);
        using var detail = await manager.GetAsync($"/api/forms/records/{recordId}", Ct);
        var stored = Assert.Single((await detail.ReadJsonAsync()).GetProperty("results").EnumerateArray());
        Assert.Equal((TestRecordIssuer.Key, "00004-00000012"), (stored.GetProperty("source").GetString(), stored.GetProperty("values")[0].GetProperty("value").GetString()));
        using var workflow = await manager.GetAsync($"/api/forms/records/{recordId}/workflow", Ct);
        var document = Assert.Single((await workflow.ReadJsonAsync()).GetProperty("documents").EnumerateArray());
        Assert.Equal((runId, "es", "Factura C 00004-00000012.pdf"), (document.GetProperty("runId").GetGuid(), document.GetProperty("culture").GetString(),
            document.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task An_issuance_in_review_is_decided_from_the_workflow_only_with_the_permission_of_its_issuer()
    {
        using var reviewer = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage, BusinessFormsPermissions.ApprovalsDecide]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage]);
        var issuer = new TestRecordIssuer();
        await using var host = IssuerHost(issuer);
        using var deciding = InHost(host, reviewer);
        using var managing = InHost(host, manager);
        var (recordId, runId) = await SeedIssueRunAsync(reviewer);
        var worker = host.Services.GetRequiredService<FormActionBackgroundService>();
        issuer.Next = new(StructuredIssueState.NeedsReview, ReviewReason: "test.issue.dateOutOfRange", Revision: 4);
        await worker.ProcessNextAsync(runId, Ct);

        var action = await WorkflowActionAsync(deciding, recordId);
        Assert.Equal(("Uncertain", "test.issue.dateOutOfRange", true), (action.GetProperty("state").GetString(), action.GetProperty("reasonCode").GetString(),
            action.GetProperty("canReview").GetBoolean()));
        Assert.False((await WorkflowActionAsync(managing, recordId)).TryGetProperty("canReview", out _));
        var decision = new { expectedRevision = action.GetProperty("revision").GetInt64(), wasSent = true };
        using var forbidden = await managing.PostAsJsonAsync($"/api/forms/records/{recordId}/actions/{runId}/review", decision, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var decided = await deciding.PostAsJsonAsync($"/api/forms/records/{recordId}/actions/{runId}/review", decision, Ct);
        Assert.Equal(HttpStatusCode.Accepted, decided.StatusCode);
        issuer.Next = new(StructuredIssueState.Pending);
        await worker.ProcessNextAsync(runId, Ct);

        var review = Assert.Single(issuer.Reviews);
        Assert.Equal((4L, true), (review.ExpectedRevision, review.Proceed));
        Assert.Equal(FormActionRunState.Running, (await RunAsync(runId)).State);
        Assert.Empty(issuer.Requests.Skip(1));
    }

    [Fact]
    public async Task A_response_with_an_issued_document_is_not_corrected_and_is_voided_when_its_reversal_is_issued()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsAmend, BusinessFormsPermissions.RulesManage,
            BusinessFormsPermissions.ApprovalsDecide, BusinessFormsPermissions.RecordsCreate]);
        var issuer = new TestRecordIssuer();
        await using var host = IssuerHost(issuer);
        using var client = InHost(host, manager);
        var (recordId, runId) = await SeedIssueRunAsync(manager);
        var definitionId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().Where(record => record.Id == recordId)
            .Select(record => record.DefinitionId).SingleAsync(Ct));
        var worker = host.Services.GetRequiredService<FormActionBackgroundService>();
        issuer.Next = new(StructuredIssueState.Issued, Result: Voucher("Factura C"));
        await worker.ProcessNextAsync(runId, Ct);

        using var correction = await client.GetAsync($"/api/forms/entries/{definitionId}/records/{recordId}", Ct);
        Assert.Equal(HttpStatusCode.Conflict, correction.StatusCode);
        Assert.Equal("Forms.Record.IssuedNotAmendable", (await correction.ReadJsonAsync()).GetProperty("code").GetString());

        using var voided = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/records/{recordId}/void", new { reason = "Se cargó mal" }, Ct);
        Assert.True(voided.IsSuccessStatusCode, voided.StatusCode.ToString());
        var reversalId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().Where(run => run.RecordId == recordId && run.Event == "Voided")
            .Select(run => run.Id).SingleAsync(Ct));
        Assert.Equal(FormRecordState.Received, await RecordStateAsync(recordId));

        await worker.ProcessNextAsync(reversalId, Ct);
        Assert.Equal(FormActionRunState.Running, (await RunAsync(reversalId)).State);
        issuer.ByRun[reversalId] = new(StructuredIssueState.Issued, Result: Voucher("Nota de crédito C"));
        factory.Clock.Advance(TimeSpan.FromSeconds(6));
        await worker.ProcessNextAsync(reversalId, Ct);

        Assert.Equal(FormActionRunState.Completed, (await RunAsync(reversalId)).State);
        Assert.Equal(FormRecordState.Voided, await RecordStateAsync(recordId));
        Assert.Equal(2, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordResult>().CountAsync(result => result.RecordId == recordId, Ct)));
    }

    private static StructuredRecordResult Voucher(string name) => new(TestRecordIssuer.Key,
        new Dictionary<string, string> { ["es"] = "Comprobante de prueba", ["en"] = "Test voucher" },
        [new("voucher", new Dictionary<string, string> { ["es"] = "Comprobante", ["en"] = "Voucher" }, StructuredRecordResultField.Text, name)]);

    private Task<FormRecordState> RecordStateAsync(Guid recordId) => factory.ExecuteDbContextAsync(db =>
        db.Set<BusinessFormRecord>().Where(record => record.Id == recordId).Select(record => record.State).SingleAsync(Ct));

    [Fact]
    public async Task A_response_from_a_channel_waits_for_its_author_to_confirm_and_not_issuing_voids_it()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var issuer = new TestRecordIssuer();
        await using var host = IssuerHost(issuer);
        using var client = InHost(host, author);
        var worker = host.Services.GetRequiredService<FormActionBackgroundService>();

        var (confirmedId, confirmedRun) = await SeedIssueRunAsync(author, enteredInApp: false);
        await worker.ProcessNextAsync(confirmedRun, Ct);
        Assert.Equal((FormActionRunState.Uncertain, "Forms.Action.AwaitingConfirmation"), ((await RunAsync(confirmedRun)).State, (await RunAsync(confirmedRun)).FailureCode));
        Assert.Empty(issuer.Requests);
        using var waiting = await client.GetAsync($"/api/forms/records/{confirmedId}/workflow", Ct);
        var confirmation = (await waiting.ReadJsonAsync()).GetProperty("confirmation");
        Assert.Equal((confirmedRun, "Factura C", "Juana Pérez", "3101.5"), (confirmation.GetProperty("runId").GetGuid(),
            confirmation.GetProperty("document").GetProperty("es").GetString(), confirmation.GetProperty("receiver").GetString(), confirmation.GetProperty("total").GetString()));

        using var issued = await client.PostAsJsonAsync($"/api/forms/records/{confirmedId}/actions/{confirmedRun}/confirmation", new { issue = true }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        issuer.Next = new(StructuredIssueState.Pending);
        await worker.ProcessNextAsync(confirmedRun, Ct);
        Assert.Equal(confirmedRun, Assert.Single(issuer.Requests).RunId);

        var (skippedId, skippedRun) = await SeedIssueRunAsync(author, enteredInApp: false);
        await worker.ProcessNextAsync(skippedRun, Ct);
        using var skipped = await client.PostAsJsonAsync($"/api/forms/records/{skippedId}/actions/{skippedRun}/confirmation", new { issue = false }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, skipped.StatusCode);
        await worker.ProcessNextAsync(skippedRun, Ct);
        Assert.Equal((FormRecordState.Voided, FormActionRunState.NotApplicable), (await RecordStateAsync(skippedId), (await RunAsync(skippedRun)).State));
        Assert.Single(issuer.Requests);
        using var again = await client.PostAsJsonAsync($"/api/forms/records/{skippedId}/actions/{skippedRun}/confirmation", new { issue = true }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    private WebApplicationFactory<Program> IssuerHost(TestRecordIssuer issuer) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IStructuredRecordIssuer>();
        services.AddSingleton<IStructuredRecordIssuer>(issuer);
    }));

    private static HttpClient InHost(WebApplicationFactory<Program> host, HttpClient source)
    {
        var client = host.CreateClient();
        foreach (var header in source.DefaultRequestHeaders)
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        return client;
    }

    private static async Task<System.Text.Json.JsonElement> WorkflowActionAsync(HttpClient client, Guid recordId)
    {
        using var workflow = await client.GetAsync($"/api/forms/records/{recordId}/workflow", Ct);
        Assert.Equal(HttpStatusCode.OK, workflow.StatusCode);
        return Assert.Single((await workflow.ReadJsonAsync()).GetProperty("actions").EnumerateArray());
    }

    private Task<BusinessFormActionRun> RunAsync(Guid runId) =>
        factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().AsNoTracking().SingleAsync(run => run.Id == runId, Ct));

    // A form copied from the issuer's standard, with an «IssueDocument» rule, a response and its run waiting.
    // Loaded in the app by default (it was confirmed before sending); one from a channel waits for «¿La emito?».
    private async Task<(Guid RecordId, Guid RunId)> SeedIssueRunAsync(HttpClient actorClient, bool enteredInApp = true)
    {
        var sheet = await SeedSheetAsync(actorClient, 0);
        var actor = Guid.Parse(actorClient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        return await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == sheet.Id, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            definition.RecordStandard(TestRecordIssuer.Key, 1, FormDraftPolicy.Serialize(new Dictionary<string, Guid> { ["first"] = sheet.Fields[0].FieldId }));
            var action = new FormRuleAction(Guid.NewGuid(), "IssueDocument", null, null, false, TestRecordIssuer.Key);
            var rule = new FormRule(Guid.NewGuid(), "Received", [], [action]);
            var ruleSet = BusinessFormRuleSet.Create(definition.Id, 1, "es", FormDraftPolicy.Serialize(new[] { rule }), false, version.SchemaJson);
            db.Set<BusinessFormRuleSet>().Add(ruleSet);
            await db.SaveChangesAsync(Ct);
            definition.SetActiveRuleSetId(ruleSet.Id);
            var now = factory.Clock.GetUtcNow().UtcDateTime;
            var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), actor, Guid.NewGuid(), now, "[]", ruleSet.Id,
                "es", enteredInApp);
            db.Set<BusinessFormRecord>().Add(record);
            db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(record.SourceReceiptId, definition.Id, version.Id, "test-issue", record.Id, record.Number, now));
            var run = BusinessFormActionRun.Create(definition.Id, record.Id, ruleSet.Id, rule.Id, action.Id, "Received", "IssueDocument", 0, null,
                FormDraftPolicy.Serialize(new FormActionSnapshot(null, null, null, [], IssuerId: TestRecordIssuer.Key)), true, now);
            db.Set<BusinessFormActionRun>().Add(run);
            await db.SaveChangesAsync(Ct);
            return (record.Id, run.Id);
        });
    }
}
