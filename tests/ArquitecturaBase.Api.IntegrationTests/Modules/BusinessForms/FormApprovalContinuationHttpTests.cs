using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Approval_continuation_keeps_the_next_pending_task_after_earlier_decisions(string culture)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        using var other = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        decider.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var seeded = await SeedContinuationAsync(author, decider, other);
        var ids = seeded.OrderedTaskIds;
        var otherId = Guid.Parse(other.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var sharedRoles = await factory.ExecuteDbContextAsync(db =>
            (from link in db.UserRoles
             where link.UserId == seeded.DeciderId || link.UserId == otherId
             join role in db.Roles on link.RoleId equals role.Id
             select role.Name!).Distinct().ToArrayAsync(Ct));
        await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().SetRolesAsync(otherId, sharedRoles, Ct));
        await ApproveContinuationTaskAsync(other, ids[0]);
        Assert.Equal(otherId, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormTask>()
            .Where(task => task.Id == ids[0]).Select(task => task.DecidedByUserId).SingleAsync(Ct)));

        using var next = await ReadContinuationAsync(decider, seeded.DefinitionId, ids[0], "Next", 1);
        AssertContinuation(await next.ReadJsonAsync(), seeded.DefinitionId, ids[0], "Next", [ids[1]], [1], 3, true);
        await FormApprovalContractCapture.WriteAsync(next, "approval-continuation-next-" + culture, Ct);
        using var previous = await ReadContinuationAsync(decider, seeded.DefinitionId, ids[3], "Previous", 1);
        AssertContinuation(await previous.ReadJsonAsync(), seeded.DefinitionId, ids[3], "Previous", [ids[2]], [2], 3, true);
        await FormApprovalContractCapture.WriteAsync(previous, "approval-continuation-previous-" + culture, Ct);
        using var ascendingPrevious = await ReadContinuationAsync(decider, seeded.DefinitionId, ids[3], "Previous", 2);
        AssertContinuation(await ascendingPrevious.ReadJsonAsync(), seeded.DefinitionId, ids[3], "Previous", [ids[1], ids[2]], [1, 2], 3, false);

        await ApproveContinuationTaskAsync(decider, ids[1]);
        using var closedAnchor = await ReadContinuationAsync(decider, seeded.DefinitionId, ids[1], "Next", 2);
        var closed = await closedAnchor.ReadJsonAsync();
        AssertContinuation(closed, seeded.DefinitionId, ids[1], "Next", [ids[2], ids[3]], [1, 2], 2, false);
        Assert.All(closed.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal(seeded.Prefix + (culture == "en" ? " Sheet" : " Planilla"), item.GetProperty("task").GetProperty("formName").GetString());
            Assert.True(item.GetProperty("task").GetProperty("canDecide").GetBoolean());
        });
        await FormApprovalContractCapture.WriteAsync(closedAnchor, "approval-continuation-closed-" + culture, Ct);
        using var beforeFirst = await ReadContinuationAsync(decider, seeded.DefinitionId, ids[2], "Previous", 1);
        AssertContinuation(await beforeFirst.ReadJsonAsync(), seeded.DefinitionId, ids[2], "Previous", [], [], 2, false);
        using var afterLast = await ReadContinuationAsync(decider, seeded.DefinitionId, ids[3], "Next", 1);
        AssertContinuation(await afterLast.ReadJsonAsync(), seeded.DefinitionId, ids[3], "Next", [], [], 2, false);
        await ApproveContinuationTaskAsync(decider, ids[2]);
        await ApproveContinuationTaskAsync(decider, ids[3]);
        foreach (var direction in new[] { "Next", "Previous" })
        {
            using var empty = await ReadContinuationAsync(decider, seeded.DefinitionId, ids[1], direction, 2);
            AssertContinuation(await empty.ReadJsonAsync(), seeded.DefinitionId, ids[1], direction, [], [], 0, false);
        }
    }

    [Fact]
    public async Task Approval_continuation_requires_decide_and_hides_foreign_own_and_unassigned_anchors()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        using var other = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        using var anonymous = factory.CreateClient();
        var seeded = await SeedContinuationAsync(author, decider, other);
        var foreign = await SeedApprovalAsync(author, decider, 10);
        var path = ContinuationPath(seeded.DefinitionId, seeded.OrderedTaskIds[0], "Next", 1);
        using var missing = await anonymous.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        using var noDecide = await author.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, noDecide.StatusCode);
        foreach (var (definition, task) in new[]
        {
            (seeded.DefinitionId, Guid.NewGuid()), (Guid.NewGuid(), seeded.OrderedTaskIds[0]),
            (seeded.DefinitionId, foreign.TaskId), (foreign.DefinitionId, seeded.OrderedTaskIds[0]),
            (seeded.DefinitionId, seeded.OwnTaskId), (seeded.DefinitionId, seeded.OtherRoleTaskId)
        })
        {
            using var hidden = await decider.GetAsync(ContinuationPath(definition, task, "Next", 1), Ct);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            Assert.Equal(FormApprovalErrors.NotFound.Code, (await hidden.ReadJsonAsync()).GetProperty("code").GetString());
        }
        using var remaining = await ReadContinuationAsync(decider, seeded.DefinitionId, seeded.OrderedTaskIds[0], "Next", 20);
        AssertContinuation(await remaining.ReadJsonAsync(), seeded.DefinitionId, seeded.OrderedTaskIds[0], "Next",
            seeded.OrderedTaskIds[1..], [2, 3, 4], 4, false);
        await factory.InTransactionAsync(async services =>
        {
            var role = "continuation-new-role-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, [BusinessFormsPermissions.ApprovalsDecide], Ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(seeded.DeciderId, [role], Ct);
        });
        using var removedRole = await decider.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.NotFound, removedRole.StatusCode);
        Assert.Equal(FormApprovalErrors.NotFound.Code, (await removedRole.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(null, 1, false)]
    [InlineData("next", 1, false)]
    [InlineData("Next", 0, false)]
    [InlineData("Previous", 101, false)]
    [InlineData("unknown", 1, false)]
    [InlineData("Next", 1, true)]
    public async Task Approval_continuation_validates_direction_batch_size_and_definition(string? direction, int pageSize, bool emptyDefinition)
    {
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        using var invalid = await decider.GetAsync(ContinuationPath(emptyDefinition ? Guid.Empty : Guid.NewGuid(), Guid.NewGuid(), direction, pageSize), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private async Task<FormApprovalContinuationSeed> SeedContinuationAsync(HttpClient author, HttpClient decider, HttpClient other)
    {
        var first = await SeedApprovalAsync(author, decider, 10);
        var otherId = Guid.Parse(other.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        return await factory.ExecuteDbContextAsync(db => FormApprovalContinuationFixture.AddAsync(db, first.TaskId, first.DeciderId, otherId, Ct));
    }
    private static string ContinuationPath(Guid definitionId, Guid taskId, string? direction, int pageSize) =>
        $"/api/forms/approvals/{taskId}/continuation?definitionId={definitionId}&pageSize={pageSize}"
        + (direction is null ? "" : "&direction=" + direction);
    private static async Task<HttpResponseMessage> ReadContinuationAsync(HttpClient decider, Guid definitionId, Guid taskId, string direction, int pageSize)
    {
        var response = await decider.GetAsync(ContinuationPath(definitionId, taskId, direction, pageSize), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }
    private static async Task ApproveContinuationTaskAsync(HttpClient decider, Guid id)
    {
        using var response = await decider.PostAsJsonAsync($"/api/forms/approvals/{id}/decisions", new { expectedRevision = 1, decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private static void AssertContinuation(JsonElement response, Guid definitionId, Guid anchor, string direction,
        Guid[] ids, int[] positions, int totalCount, bool hasMore)
    {
        Assert.Equal(new[] { "anchorTaskId", "definitionId", "direction", "hasMore", "items", "totalCount" },
            response.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(anchor, response.GetProperty("anchorTaskId").GetGuid());
        Assert.Equal(definitionId, response.GetProperty("definitionId").GetGuid());
        Assert.Equal(direction, response.GetProperty("direction").GetString());
        Assert.Equal(totalCount, response.GetProperty("totalCount").GetInt32());
        Assert.Equal(hasMore, response.GetProperty("hasMore").GetBoolean());
        var items = response.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(ids, items.Select(item => item.GetProperty("task").GetProperty("id").GetGuid()));
        Assert.Equal(positions, items.Select(item => item.GetProperty("position").GetInt32()));
        Assert.All(items, item =>
        {
            Assert.Equal(definitionId, item.GetProperty("task").GetProperty("definitionId").GetGuid());
            Assert.Equal("Open", item.GetProperty("task").GetProperty("state").GetString());
        });
    }
}
