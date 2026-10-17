using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public async Task Workflow_uses_the_actual_approval_request_time_before_and_after_the_decision(string decision)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var seeded = await SeedApprovalAsync(author, decider, 12, TimeSpan.FromMinutes(3));
        var requestedAtUtc = new DateTime(2026, 10, 16, 12, 3, 0, DateTimeKind.Utc);
        var route = "/api/forms/records/" + seeded.RecordId + "/workflow";

        using var pending = await author.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var open = await pending.ReadJsonAsync();
        var task = open.GetProperty("task");
        Assert.Equal("PendingApproval", open.GetProperty("recordState").GetString());
        Assert.Equal("Open", task.GetProperty("state").GetString());
        Assert.Equal(requestedAtUtc, task.GetProperty("requestedAtUtc").GetDateTime());
        var approval = Assert.Single(open.GetProperty("actions").EnumerateArray());
        Assert.Equal("RequestApproval", approval.GetProperty("kind").GetString());
        Assert.Equal(requestedAtUtc.AddMinutes(-3), approval.GetProperty("createdAtUtc").GetDateTime());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("decidedBy").ValueKind);
        Assert.DoesNotContain(open.GetProperty("actions").EnumerateArray(), action =>
            action.GetProperty("event").GetString() is "Approved" or "Rejected");
        await FormWorkflowContractCapture.WriteAsync(pending, "approval-pending-d2-" + decision.ToLowerInvariant() + "-es", Ct);

        using var response = await decider.PostAsJsonAsync("/api/forms/approvals/" + seeded.TaskId + "/decisions",
            new { expectedRevision = 1, decision, reason = decision == "Rejected" ? "Revisá la visita" : null }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var completed = await author.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var closed = await completed.ReadJsonAsync();
        Assert.Equal(decision, closed.GetProperty("recordState").GetString());
        Assert.Equal(requestedAtUtc, closed.GetProperty("task").GetProperty("requestedAtUtc").GetDateTime());
        Assert.Equal(seeded.DeciderId, closed.GetProperty("task").GetProperty("decidedBy").GetProperty("userId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("task").GetProperty("decidedAtUtc").ValueKind);
        Assert.Equal(1, closed.GetProperty("ruleRevision").GetInt64());
        Assert.Contains(closed.GetProperty("actions").EnumerateArray(), action => action.GetProperty("event").GetString() == decision);
        Assert.DoesNotContain(closed.GetProperty("actions").EnumerateArray(), action =>
            action.GetProperty("event").GetString() == (decision == "Approved" ? "Rejected" : "Approved"));
        await FormWorkflowContractCapture.WriteAsync(completed, "approval-" + decision.ToLowerInvariant() + "-d2-es", Ct);
    }

    [Fact]
    public async Task A_failed_approval_request_remains_observable_without_a_task_or_a_pending_response()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedWorkflowRunAsync(reader, FormActionRunState.Waiting, "RequestApproval");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IFormActionDispatchService>().ReserveAsync(seeded.RunId, Ct);
            Assert.True(result.IsSuccess);
            Assert.Null(result.Value);
        }

        using var response = await reader.GetAsync("/api/forms/records/" + seeded.RecordId + "/workflow", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var workflow = await response.ReadJsonAsync();
        Assert.Equal("Received", workflow.GetProperty("recordState").GetString());
        Assert.Equal(JsonValueKind.Null, workflow.GetProperty("task").ValueKind);
        var action = Assert.Single(workflow.GetProperty("actions").EnumerateArray());
        Assert.Equal(seeded.RunId, action.GetProperty("id").GetGuid());
        Assert.Equal("Received", action.GetProperty("event").GetString());
        Assert.Equal("RequestApproval", action.GetProperty("kind").GetString());
        Assert.Equal("Failed", action.GetProperty("state").GetString());
        Assert.Equal(FormRuleErrors.RoleUnavailable.Code, action.GetProperty("reasonCode").GetString());
        Assert.False(action.GetProperty("canRetry").GetBoolean());
        Assert.False(action.GetProperty("canConsult").GetBoolean());
        await FormWorkflowContractCapture.WriteAsync(response, "approval-failed-without-task-d2-es", Ct);
    }

    [Fact]
    public async Task A_response_without_a_channel_origin_does_not_invent_a_sender()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedWorkflowRunAsync(reader, FormActionRunState.Completed);
        using var response = await reader.GetAsync("/api/forms/records/" + seeded.RecordId, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await response.ReadJsonAsync()).GetProperty("origin").ValueKind);
    }
}
