using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Rule_routes_require_only_rule_management_and_an_authenticated_session()
    {
        using var anonymous = factory.CreateClient();
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read,BusinessFormsPermissions.Manage]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage]);
        var id = Guid.NewGuid();
        foreach (var suffix in new[] { "", "/options", "/revisions", "/revisions/1" })
        {
            var route = $"/api/forms/definitions/{id}/rules" + suffix;
            using var missing = await anonymous.GetAsync(route,Ct); Assert.Equal(HttpStatusCode.Unauthorized,missing.StatusCode);
            using var denied = await designer.GetAsync(route,Ct); Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            using var allowed = await manager.GetAsync(route,Ct); Assert.Equal(HttpStatusCode.NotFound,allowed.StatusCode);
        }
        using var missingSave = await anonymous.PostAsJsonAsync($"/api/forms/definitions/{id}/rules",new { expectedRevision = 0, rules = Array.Empty<object>() },Ct);
        Assert.Equal(HttpStatusCode.Unauthorized,missingSave.StatusCode);
        using var deniedSave = await designer.PostAsJsonAsync($"/api/forms/definitions/{id}/rules",new { expectedRevision = 0, rules = Array.Empty<object>() },Ct);
        Assert.Equal(HttpStatusCode.Forbidden,deniedSave.StatusCode);
    }
    [Fact]
    public async Task Rule_revisions_keep_the_original_language_and_do_not_change_the_draft_or_published_versions()
    {
        using var designer = await AdminAsync();
        var created = await CreateAsync(designer,Payload("Reglas de visitas"));
        var definition = created.GetProperty("definition");
        var id = definition.GetProperty("id").GetGuid();
        var draftRevision = definition.GetProperty("revision").GetInt64();
        var route = $"/api/forms/definitions/{id}/rules";
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage]);
        using var empty = await manager.GetAsync(route,Ct);
        Assert.Equal(HttpStatusCode.OK,empty.StatusCode);
        Assert.Equal(0,(await empty.ReadJsonAsync()).GetProperty("revision").GetInt64());
        await FormRuleContractCapture.WriteAsync(empty,"empty-es",Ct);
        using var options = await manager.GetAsync(route + "/options",Ct);
        Assert.Equal(HttpStatusCode.OK,options.StatusCode);
        await FormRuleContractCapture.WriteAsync(options,"options-es",Ct);
        Assert.Equal("Nombre",(await options.ReadJsonAsync()).GetProperty("questions")[0].GetProperty("label").GetProperty("es").GetString());
        var body = new { expectedRevision = 0, rules = new[] { new { id = Guid.NewGuid(), @event = "Received", conditions = Array.Empty<object>(), actions = new[] { new { id = Guid.NewGuid(), kind = "NotifyAuthor", text = new { es = "Recibimos {número}: {Nombre}" }, includePdf = false } } } } };
        using var saved = await manager.PostAsJsonAsync(route,body,Ct);
        Assert.Equal(HttpStatusCode.Created,saved.StatusCode);
        Assert.EndsWith("/revisions/1",saved.Headers.Location!.ToString(),StringComparison.Ordinal);
        await FormRuleContractCapture.WriteAsync(saved,"saved-es",Ct);
        var first = await saved.ReadJsonAsync();
        Assert.Equal(1,first.GetProperty("revision").GetInt64());
        Assert.False(first.GetProperty("rules")[0].GetProperty("actions")[0].GetProperty("text").TryGetProperty("en",out _));
        using var stale = await manager.PostAsJsonAsync(route,body,Ct);
        Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        Assert.Equal(FormRuleErrors.RevisionConflict.Code,(await stale.ReadJsonAsync()).GetProperty("code").GetString());
        await FormRuleContractCapture.WriteAsync(stale,"conflict-es",Ct);
        using var disabled = await manager.PostAsJsonAsync(route,new { expectedRevision = 1, rules = Array.Empty<object>() },Ct);
        Assert.Equal(HttpStatusCode.Created,disabled.StatusCode);
        using var history = await manager.GetAsync(route + "/revisions?pageSize=1",Ct);
        Assert.Equal(HttpStatusCode.OK,history.StatusCode);
        Assert.Equal(2,(await history.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        Assert.Equal(2,(await history.ReadJsonAsync()).GetProperty("items")[0].GetProperty("revision").GetInt64());
        var changes = (await history.ReadJsonAsync()).GetProperty("items")[0].GetProperty("changes");
        Assert.Equal(0,changes.GetProperty("addedRules").GetInt32());
        Assert.Equal(0,changes.GetProperty("updatedRules").GetInt32());
        Assert.Equal(1,changes.GetProperty("removedRules").GetInt32());
        Assert.False(changes.GetProperty("orderChanged").GetBoolean());
        await FormRuleContractCapture.WriteAsync(history,"history-es",Ct);
        using var previous = await manager.GetAsync(route + "/revisions/1",Ct);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(first.GetRawText()), JsonNode.Parse((await previous.ReadJsonAsync()).GetRawText())));
        await FormRuleContractCapture.WriteAsync(previous,"revision-es",Ct);
        manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var englishOptions = await manager.GetAsync(route + "/options",Ct);
        await FormRuleContractCapture.WriteAsync(englishOptions,"options-en",Ct);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var stored = await db.Set<BusinessFormDefinition>().SingleAsync(value=>value.Id == id,Ct);
            Assert.Equal(draftRevision,stored.Revision);
            Assert.Null(stored.ActiveVersionId);
            Assert.Equal(0,await db.Set<BusinessFormVersion>().CountAsync(value=>value.DefinitionId == id,Ct));
            var revisions = await db.Set<BusinessFormRuleSet>().Where(value=>value.DefinitionId == id).OrderBy(value=>value.Revision).ToArrayAsync(Ct);
            Assert.Equal(2,revisions.Length);
            Assert.Equal(revisions[1].Id,stored.ActiveRuleSetId);
            Assert.Contains("Nombre",revisions[0].QuestionSchemaJson,StringComparison.Ordinal);
        });
    }
    [Fact]
    public async Task Removed_rule_permissions_and_missing_roles_cannot_change_a_revision()
    {
        using var designer = await AdminAsync();
        var created = await CreateAsync(designer,Payload("Permisos de reglas"));
        var id = created.GetProperty("definition").GetProperty("id").GetGuid();
        var route = $"/api/forms/definitions/{id}/rules";
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage]);
        using var unavailable = await manager.PostAsJsonAsync(route,new { expectedRevision = 0, rules = new[] { new { id = Guid.NewGuid(), @event = "Received", conditions = Array.Empty<object>(), actions = new[] { new { id = Guid.NewGuid(), kind = "RequestApproval", roleId = Guid.NewGuid() } } } } },Ct);
        Assert.Equal(HttpStatusCode.BadRequest,unavailable.StatusCode);
        Assert.Equal(FormRuleErrors.RoleUnavailable.Code,(await unavailable.ReadJsonAsync()).GetProperty("code").GetString());
        await FormRuleContractCapture.WriteAsync(unavailable,"role-unavailable-es",Ct);
        var actor = Guid.Parse(manager.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var role = await factory.ExecuteDbContextAsync(db=>db.UserRoles.Where(link=>link.UserId == actor).Join(db.Roles,link=>link.RoleId,value=>value.Id,(link,value)=>new {value.Id,value.Name}).SingleAsync(Ct));
        using var revoked = await designer.PutAsJsonAsync("/api/roles/" + role.Id,new { name = role.Name, description = (string?)null, permissions = Array.Empty<string>() },Ct);
        Assert.Equal(HttpStatusCode.NoContent,revoked.StatusCode);
        using var deniedRead = await manager.GetAsync(route,Ct); Assert.Equal(HttpStatusCode.Forbidden,deniedRead.StatusCode);
        using var deniedWrite = await manager.PostAsJsonAsync(route,new { expectedRevision = 0, rules = Array.Empty<object>() },Ct); Assert.Equal(HttpStatusCode.Forbidden,deniedWrite.StatusCode);
        Assert.Equal(0,await factory.ExecuteDbContextAsync(db=>db.Set<BusinessFormRuleSet>().CountAsync(value=>value.DefinitionId == id,Ct)));
    }

    [Fact]
    public async Task Rule_history_summaries_use_the_immediate_predecessor_from_the_same_definition_outside_the_page()
    {
        using var designer = await AdminAsync();
        var created = await CreateAsync(designer, Payload("Historial resumido"));
        var id = created.GetProperty("definition").GetProperty("id").GetGuid();
        var other = await CreateAsync(designer, Payload("Otra definición"));
        var otherId = other.GetProperty("definition").GetProperty("id").GetGuid();
        var first = new FormRule(Guid.NewGuid(), "Received", [],
            [new(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string,string> { ["es"] = "Primero" }, false)]);
        var second = first with { Id = Guid.NewGuid(), Actions = [first.Actions[0] with { Id = Guid.NewGuid() }] };
        var added = first with { Id = Guid.NewGuid(), Actions = [first.Actions[0] with { Id = Guid.NewGuid() }] };
        var edited = first with { Actions = [first.Actions[0] with { Text = new Dictionary<string,string> { ["es"] = "Editado" } }] };
        var unrelated = first with { Id = Guid.NewGuid(), Actions = [first.Actions[0] with { Id = Guid.NewGuid() }] };
        await factory.ExecuteDbContextAsync(async db =>
        {
            var rows = new IReadOnlyList<FormRule>[] { new[] { first, second }, new[] { added, first, second }, new[] { second, first, added }, new[] { second, edited } };
            for (var index = 0; index < rows.Length; index++)
            {
                db.Set<BusinessFormRuleSet>().Add(BusinessFormRuleSet.Create(id, index + 1, "es",
                    FormDraftPolicy.Serialize(rows[index]), false, "{}"));
                db.Set<BusinessFormRuleSet>().Add(BusinessFormRuleSet.Create(otherId, index + 1, "es",
                    FormDraftPolicy.Serialize(new[] { unrelated }), false, "{}"));
            }
            await db.SaveChangesAsync(Ct);
        });
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage]);
        var route = $"/api/forms/definitions/{id}/rules/revisions";
        using var latest = await manager.GetAsync(route + "?pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        var latestBody = await latest.ReadJsonAsync();
        Assert.Equal(4, latestBody.GetProperty("totalCount").GetInt32());
        var latestRow = latestBody.GetProperty("items")[0];
        Assert.Equal(4, latestRow.GetProperty("revision").GetInt64());
        var latestChanges = latestRow.GetProperty("changes");
        Assert.Equal(0, latestChanges.GetProperty("addedRules").GetInt32());
        Assert.Equal(1, latestChanges.GetProperty("updatedRules").GetInt32());
        Assert.Equal(1, latestChanges.GetProperty("removedRules").GetInt32());
        Assert.False(latestChanges.GetProperty("orderChanged").GetBoolean());
        Assert.False(latestRow.TryGetProperty("rulesJson", out _));
        Assert.False(latestRow.TryGetProperty("previousRulesJson", out _));

        using var initial = await manager.GetAsync(route + "?page=4&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var initialChanges = (await initial.ReadJsonAsync()).GetProperty("items")[0].GetProperty("changes");
        Assert.Equal(2, initialChanges.GetProperty("addedRules").GetInt32());
        Assert.Equal(0, initialChanges.GetProperty("updatedRules").GetInt32());
        Assert.Equal(0, initialChanges.GetProperty("removedRules").GetInt32());
        Assert.False(initialChanges.GetProperty("orderChanged").GetBoolean());

        await factory.ExecuteScopeAsync(async services =>
        {
            var recorder = new FormResponseCommandRecorder();
            await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(
                services.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
            var reader = new FormRuleReader(db);
            var page = await reader.ListRevisionsAsync(id, new() { Page = 2, PageSize = 1 }, Ct);
            var revision = Assert.Single(page.Items);
            Assert.Equal(3, revision.Revision);
            Assert.Equal(new FormRuleChangesResponse(0, 0, 0, true),
                FormRuleHistoryPolicy.Compare(FormDraftPolicy.Read<FormRule[]>(revision.RulesJson),
                    FormDraftPolicy.Read<FormRule[]>(Assert.IsType<string>(revision.PreviousRulesJson))));
            Assert.Equal(2, recorder.Commands.Count(command => command.Sql.Contains("BusinessFormRuleSets", StringComparison.Ordinal)));
            var dataQuery = Assert.Single(recorder.Commands, command => command.Sql.Contains("LIMIT", StringComparison.Ordinal));
            Assert.Contains("RulesJson", dataQuery.Sql, StringComparison.Ordinal);
            Assert.Contains("DefinitionId", dataQuery.Sql, StringComparison.Ordinal);

            recorder.Commands.Clear();
            var ascending = await reader.ListRevisionsAsync(id, new() { Page = 3, PageSize = 1, Sort = "revision" }, Ct);
            var sameRevision = Assert.Single(ascending.Items);
            Assert.Equal(revision.Revision, sameRevision.Revision);
            Assert.Equal(revision.PreviousRulesJson, sameRevision.PreviousRulesJson);
            Assert.Equal(2, recorder.Commands.Count(command => command.Sql.Contains("BusinessFormRuleSets", StringComparison.Ordinal)));
        });
    }
}
