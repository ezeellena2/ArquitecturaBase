using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    private async Task<HttpClient> OperatorAsync(IReadOnlyCollection<string> codes)
    {
        var actor = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>(); var name = "forms-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(name, null, codes, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("forms-operator")).Value, null, false, null, "es", Ct);
            await users.SetRolesAsync(user.Id, [name], Ct); return user.Id;
        });
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString()); return client;
    }
    [Fact]
    public async Task Read_and_manage_permissions_and_own_attempt_isolation_are_enforced()
    {
        using var anonymous = factory.CreateClient(); using var noSession = await anonymous.GetAsync("/api/forms/definitions", Ct); Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        using var manageOnly = await OperatorAsync([BusinessFormsPermissions.Manage]);
        using var denied = await manageOnly.PostAsJsonAsync("/api/forms/definitions", Payload(), Ct); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var readonlyUser = await OperatorAsync([BusinessFormsPermissions.Read]);
        using var deniedWrite = await readonlyUser.PostAsJsonAsync("/api/forms/definitions", Payload(), Ct); Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
        using var admin = await AdminAsync(); var payload = Payload(); var original = await CreateAsync(admin, payload);
        using var detail = await readonlyUser.GetAsync("/api/forms/definitions/" + original.GetProperty("definition").GetProperty("id").GetGuid(), Ct);
        Assert.False((await detail.ReadJsonAsync()).GetProperty("capabilities").GetProperty("canSave").GetBoolean());
        using var foreign = await readonlyUser.GetAsync("/api/forms/draft-changes/" + original.GetProperty("requestId").GetGuid(), Ct); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var otherWriter = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        payload["prefix"] = "S" + Guid.NewGuid().ToString("N")[..10];
        var own = await CreateAsync(otherWriter, payload); Assert.NotEqual(original.GetProperty("definition").GetProperty("id").GetGuid(), own.GetProperty("definition").GetProperty("id").GetGuid());
        var actorId = Guid.Parse(otherWriter.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var role = await factory.ExecuteDbContextAsync(db => db.UserRoles.Where(link => link.UserId == actorId)
            .Join(db.Roles, link => link.RoleId, value => value.Id, (link, value) => new { value.Id, value.Name }).SingleAsync(Ct));
        using var revoked = await admin.PutAsJsonAsync("/api/roles/" + role.Id, new { name = role.Name, description = (string?)null, permissions = new[] { BusinessFormsPermissions.Read } }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var ownRead = await otherWriter.GetAsync("/api/forms/draft-changes/" + own.GetProperty("requestId").GetGuid(), Ct); Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);
        using var cannotWrite = await otherWriter.PostAsJsonAsync("/api/forms/definitions", Payload(), Ct); Assert.Equal(HttpStatusCode.Forbidden, cannotWrite.StatusCode);
    }
    [Fact]
    public async Task Failed_commit_rolls_back_definition_and_attempt_and_the_same_uuid_can_be_replayed()
    {
        using var admin = await AdminAsync(); var probe = new CommitFailureProbe();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => FailingCommitUnitOfWork.Replace(services, probe)));
        using var failing = host.CreateClient(); failing.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, admin.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var payload = Payload("Rollback proof");
        using var failed = await failing.PostAsJsonAsync("/api/forms/definitions", payload, Ct); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); Assert.True(probe.RolledBackBeforeLeaving);
        using var missing = await admin.GetAsync("/api/forms/draft-changes/" + payload["requestId"]!.GetValue<string>(), Ct); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var list = await admin.GetAsync("/api/forms/definitions?search=Rollback%20proof", Ct); Assert.Equal(0, (await list.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        var saved = await CreateAsync(admin, payload); Assert.Equal(1, saved.GetProperty("definition").GetProperty("revision").GetInt64());
    }
    [Fact]
    public async Task Expired_results_are_purged_as_tracked_rows_and_tombstones_prevent_uuid_reuse()
    {
        using var admin = await AdminAsync(); var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, admin.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var payload = Payload("Retention proof"); var saved = await CreateAsync(client, payload); var requestId = saved.GetProperty("requestId").GetGuid();
        clock.Advance(TimeSpan.FromDays(31));
        using var expired = await client.GetAsync("/api/forms/draft-changes/" + requestId, Ct); Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Equal("Resultado vencido", (await expired.ReadJsonAsync()).GetProperty("title").GetString());
        await using (var scope = host.Services.CreateAsyncScope()) Assert.True((await scope.ServiceProvider.GetRequiredService<IFormDraftRetentionService>().PurgeExpiredResultsAsync(Ct)).IsSuccess);
        var row = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>().SingleAsync(x => x.RequestId == requestId, Ct));
        Assert.Null(row.ProtectedResult); Assert.NotNull(row.ModifiedAtUtc);
        using var repeated = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct); Assert.Equal(HttpStatusCode.Gone, repeated.StatusCode);
        Assert.Equal("Forms.Draft.ChangeExpired", (await repeated.ReadJsonAsync()).GetProperty("code").GetString());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_hosts_contend_on_postgres_and_commit_only_one_revision(bool sameRequest)
    {
        using var first = await AdminAsync(); var original = await CreateAsync(first, Payload()); var id = original.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = factory.WithWebHostBuilder(_ => { }); using var second = host.CreateClient();
        second.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, first.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        await using var connection = new NpgsqlConnection(factory.ConnectionString); await connection.OpenAsync(Ct);
        await using var held = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended('forms:definitions', 0))", connection); await held.ExecuteNonQueryAsync(Ct);
        var a = EditPayload(original); a["name"] = new JsonObject { ["es"] = "Concurrent A", ["en"] = "Concurrent A" }; var b = sameRequest ? a.DeepClone().AsObject() : EditPayload(original); if (!sameRequest) b["name"] = new JsonObject { ["es"] = "Concurrent B", ["en"] = "Concurrent B" };
        var left = first.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", a, Ct); var right = second.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", b, Ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await using var waiting = new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted", connection);
            while ((long)(await waiting.ExecuteScalarAsync(timeout.Token))! < 2) await Task.Delay(10, timeout.Token);
            Assert.False(left.IsCompleted); Assert.False(right.IsCompleted);
            using var pending = await first.GetAsync("/api/forms/draft-changes/" + a["requestId"]!.GetValue<string>(), Ct); Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
        }
        finally { await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended('forms:definitions', 0))", connection); await release.ExecuteNonQueryAsync(Ct); }
        var results = await Task.WhenAll(left, right); using var leftResponse = results[0]; using var rightResponse = results[1];
        Assert.Equal(sameRequest ? new[] { HttpStatusCode.OK, HttpStatusCode.OK } : new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, results.Select(result => result.StatusCode).Order());
        if (sameRequest) Assert.Equal((await leftResponse.ReadJsonAsync()).GetRawText(), (await rightResponse.ReadJsonAsync()).GetRawText());
        using var detail = await second.GetAsync($"/api/forms/definitions/{id}", Ct); Assert.Equal(2, (await detail.ReadJsonAsync()).GetProperty("definition").GetProperty("revision").GetInt64());
        Assert.Equal(2, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>().CountAsync(x => x.DefinitionId == id, Ct)));
    }
}
