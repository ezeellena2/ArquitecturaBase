using System.Globalization;
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

    // «En cola»: while its issuer waits for its provider, the action says why, when it tries again and how many times it tried; once
    // it is pending without waiting, or for anything else, it says none of that.
    [Fact]
    public async Task An_issuance_waiting_for_its_provider_shows_in_the_workflow_when_it_tries_again_and_how_many_times_it_tried()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage]);
        var issuer = new TestRecordIssuer();
        await using var host = IssuerHost(issuer);
        using var client = InHost(host, manager);
        var (recordId, runId) = await SeedIssueRunAsync(manager);
        var worker = host.Services.GetRequiredService<FormActionBackgroundService>();
        var later = factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(10);

        issuer.Next = new(StructuredIssueState.Pending, "test.issue.unavailable", WaitingForProvider: true, NextCheckAtUtc: later) { Attempts = 3 };
        await worker.ProcessNextAsync(runId, Ct);
        var waiting = await WorkflowActionAsync(client, recordId);
        Assert.Equal(("Running", "test.issue.unavailable", later, 3), (waiting.GetProperty("state").GetString(), waiting.GetProperty("reasonCode").GetString(),
            waiting.GetProperty("nextAttemptAtUtc").GetDateTime(), waiting.GetProperty("attempts").GetInt32()));

        issuer.Current = new(StructuredIssueState.Pending);
        factory.Clock.Advance(TimeSpan.FromMinutes(10));
        await worker.ProcessNextAsync(runId, Ct);
        var issuing = await WorkflowActionAsync(client, recordId);
        Assert.Equal("Running", issuing.GetProperty("state").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, issuing.GetProperty("reasonCode").ValueKind);
        Assert.False(issuing.TryGetProperty("nextAttemptAtUtc", out _));
        Assert.False(issuing.TryGetProperty("attempts", out _));
    }

    // «Rechazada» and «No se pudo anular»: the stable code stays in reasonCode and the reason is what the provider said.
    [Fact]
    public async Task A_rejected_issuance_or_reversal_shows_in_the_workflow_what_its_provider_said()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsAmend, BusinessFormsPermissions.RulesManage,
            BusinessFormsPermissions.ApprovalsDecide, BusinessFormsPermissions.RecordsCreate]);
        var issuer = new TestRecordIssuer();
        await using var host = IssuerHost(issuer);
        using var client = InHost(host, manager);
        var worker = host.Services.GetRequiredService<FormActionBackgroundService>();

        var (rejectedId, rejectedRun) = await SeedIssueRunAsync(manager);
        issuer.Next = new(StructuredIssueState.Rejected, "test.issue.10015") { FailureDetail = "10015 · El número de documento del receptor no es válido." };
        await worker.ProcessNextAsync(rejectedRun, Ct);
        var rejected = await WorkflowActionAsync(client, rejectedId);
        Assert.Equal(("Failed", "test.issue.10015", "10015 · El número de documento del receptor no es válido."),
            (rejected.GetProperty("state").GetString(), rejected.GetProperty("reasonCode").GetString(), rejected.GetProperty("reason").GetString()));
        Assert.False(rejected.TryGetProperty("nextAttemptAtUtc", out _));

        var (issuedId, issuedRun) = await SeedIssueRunAsync(manager);
        var definitionId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().Where(record => record.Id == issuedId)
            .Select(record => record.DefinitionId).SingleAsync(Ct));
        issuer.Next = new(StructuredIssueState.Issued, Result: Voucher("Factura C"));
        await worker.ProcessNextAsync(issuedRun, Ct);
        using var voided = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/records/{issuedId}/void", new { reason = "Se cargó mal" }, Ct);
        Assert.True(voided.IsSuccessStatusCode, voided.StatusCode.ToString());
        var reversalId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().Where(run => run.RecordId == issuedId && run.Event == "Voided")
            .Select(run => run.Id).SingleAsync(Ct));
        issuer.ReversalResult = new(StructuredIssueState.Rejected, "test.issue.10197") { FailureDetail = "10197 · El comprobante asociado no es válido." };
        await worker.ProcessNextAsync(reversalId, Ct);

        using var workflow = await client.GetAsync($"/api/forms/records/{issuedId}/workflow", Ct);
        var reversal = (await workflow.ReadJsonAsync()).GetProperty("actions").EnumerateArray().Single(action => action.GetProperty("event").GetString() == "Voided");
        Assert.Equal(("Failed", "test.issue.10197", "10197 · El comprobante asociado no es válido."),
            (reversal.GetProperty("state").GetString(), reversal.GetProperty("reasonCode").GetString(), reversal.GetProperty("reason").GetString()));
        Assert.Equal(FormRecordState.Received, await RecordStateAsync(issuedId));
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
        Assert.Equal("Lo decide el sistema.", confirmation.GetProperty("info").GetProperty("es").GetString());

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

        // «No emitir» from a button of a channel decides the same and hands back, once, what the channel answers to the author.
        var (repliedId, repliedRun) = await SeedIssueRunAsync(author, enteredInApp: false);
        await worker.ProcessNextAsync(repliedRun, Ct);
        var name = await factory.ExecuteDbContextAsync(async db =>
        {
            var record = await db.Set<BusinessFormRecord>().AsNoTracking().SingleAsync(item => item.Id == repliedId, Ct);
            var prefix = await db.Set<BusinessFormDefinition>().Where(item => item.Id == record.DefinitionId).Select(item => item.Prefix).SingleAsync(Ct);
            return prefix + "-" + record.Number.ToString("D6", CultureInfo.InvariantCulture);
        });
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied, $"No la emitimos. {name} quedó anulada."),
            await ReplyAsync(host, repliedRun, authorId, "skip"));
        Assert.Equal(new StructuredRecordActionReply(StructuredRecordActionReplyState.Ignored), await ReplyAsync(host, repliedRun, authorId, "skip"));
        await worker.ProcessNextAsync(repliedRun, Ct);
        Assert.Equal((FormRecordState.Voided, FormActionRunState.NotApplicable), (await RecordStateAsync(repliedId), (await RunAsync(repliedRun)).State));
        Assert.Single(issuer.Requests);
    }

    // A button of a channel reaches the owner in its own scope, as the channel hands it over.
    private static async Task<StructuredRecordActionReply> ReplyAsync(WebApplicationFactory<Program> host, Guid runId, Guid recipientUserId, string choiceId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<IStructuredRecordActionReplySink>().Single().ReplyAsync(runId, recipientUserId, choiceId, Ct);
    }

    [Fact]
    public async Task A_form_that_issues_asks_its_issuer_for_the_preview_of_the_answers_before_sending_and_saves_nothing()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var issuer = new TestRecordIssuer();
        await using var host = IssuerHost(issuer);
        using var client = InHost(host, author);
        var (definitionId, versionId, fieldId) = await SeedIssueFormAsync(author);

        using var entry = await client.GetAsync($"/api/forms/entries/{definitionId}", Ct);
        Assert.True((await entry.ReadJsonAsync()).GetProperty("issuePreview").GetBoolean());
        var answers = new object[] { new { fieldId, value = "3101.5" } };
        issuer.Preview = new(new Dictionary<string, string> { ["es"] = "Factura C", ["en"] = "Invoice C" }, "Juana Pérez", "3101.5")
        {
            Info = new Dictionary<string, string> { ["es"] = "Lo decide el sistema.", ["en"] = "The system decides it." },
            Amounts = [new(new Dictionary<string, string> { ["es"] = "Importe neto gravado", ["en"] = "Taxed net amount" }, "2563.22")],
        };
        using var preview = await client.PostAsJsonAsync($"/api/forms/entries/{definitionId}/issue-preview", new { versionId, answers }, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var body = await preview.ReadJsonAsync();
        Assert.Equal(("Factura C", "Juana Pérez", "3101.5"), (body.GetProperty("document").GetProperty("es").GetString(),
            body.GetProperty("receiver").GetString(), body.GetProperty("total").GetString()));
        Assert.Equal(("Lo decide el sistema.", "Importe neto gravado", "2563.22"), (body.GetProperty("info").GetProperty("es").GetString(),
            body.GetProperty("amounts")[0].GetProperty("label").GetProperty("es").GetString(), body.GetProperty("amounts")[0].GetProperty("value").GetString()));
        var datum = Assert.Single(issuer.Previewed!);
        Assert.Equal(("first", "3101.5"), (datum.Key, datum.Value));

        issuer.Preview = new(null, null, null, FormActionErrors.IssueDataUnavailable.Code);
        using var failed = await client.PostAsJsonAsync($"/api/forms/entries/{definitionId}/issue-preview", new { versionId, answers }, Ct);
        var failure = await failed.ReadJsonAsync();
        Assert.Equal(FormActionErrors.IssueDataUnavailable.Code, failure.GetProperty("failureCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(failure.GetProperty("failureMessage").GetString()));
        Assert.Empty(issuer.Requests);
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().CountAsync(record => record.DefinitionId == definitionId, Ct)));

        var plain = await SeedSheetAsync(author, 0);
        using var plainEntry = await client.GetAsync($"/api/forms/entries/{plain.Id}", Ct);
        Assert.False((await plainEntry.ReadJsonAsync()).TryGetProperty("issuePreview", out _));
        var plainVersion = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(item => item.Id == plain.Id)
            .Select(item => item.ActiveVersionId!.Value).SingleAsync(Ct));
        using var none = await client.PostAsJsonAsync($"/api/forms/entries/{plain.Id}/issue-preview",
            new { versionId = plainVersion, answers = Array.Empty<object>() }, Ct);
        Assert.Equal((HttpStatusCode.NotFound, FormEntryErrors.NoIssuance.Code), (none.StatusCode, (await none.ReadJsonAsync()).GetProperty("code").GetString()));
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

    // A form copied from the standard of the issuer (its first question, a decimal, holds «first») with an «IssueDocument» rule.
    private async Task<(Guid DefinitionId, Guid VersionId, Guid FieldId)> SeedIssueFormAsync(HttpClient actorClient)
    {
        var sheet = await SeedSheetAsync(actorClient, 0);
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
            await db.SaveChangesAsync(Ct);
            return (definition.Id, version.Id, sheet.Fields[0].FieldId);
        });
    }

    private Task<BusinessFormActionRun> RunAsync(Guid runId) =>
        factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().AsNoTracking().SingleAsync(run => run.Id == runId, Ct));

    // A form copied from the issuer's standard, with an «IssueDocument» rule, a response and its run waiting.
    // Loaded in the app by default (it was confirmed before sending); one from a channel waits for «¿La emito?».
    private async Task<(Guid RecordId, Guid RunId)> SeedIssueRunAsync(HttpClient actorClient, bool enteredInApp = true)
    {
        var (definitionId, _, _) = await SeedIssueFormAsync(actorClient);
        var actor = Guid.Parse(actorClient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        return await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == definitionId, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            var ruleSet = await db.Set<BusinessFormRuleSet>().SingleAsync(item => item.Id == definition.ActiveRuleSetId, Ct);
            var rule = FormDraftPolicy.Read<FormRule[]>(ruleSet.RulesJson).Single();
            var action = rule.Actions.Single();
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
