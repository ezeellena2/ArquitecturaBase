using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormMissingRequestHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Missing_requests_are_grouped_privately_and_managed_without_forms_read(string culture)
    {
        var scenario = await FormMissingRequestScenario.CreateAsync(factory,culture,Ct);
        var firstRequest = await scenario.PrepareAsync(ct:Ct);
        var first = await scenario.CaptureAsync(firstRequest,Ct); Assert.True(first.IsSuccess);
        var samePerson = await scenario.CaptureAsync(await scenario.PrepareAsync(existing:first.Value.TopicId,ct:Ct),Ct); Assert.True(samePerson.IsSuccess);
        var otherPerson = await scenario.CaptureAsync(await scenario.PrepareAsync(scenario.OtherSubjectId,first.Value.TopicId,Ct),Ct); Assert.True(otherPerson.IsSuccess);
        using var manager = scenario.Client(scenario.ManagerId); manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var list = await manager.GetAsync("/api/forms/missing-requests?search=" + scenario.Suffix,Ct);
        Assert.Equal(HttpStatusCode.OK,list.StatusCode); Assert.Contains("no-store",list.Headers.CacheControl?.ToString() ?? "",StringComparison.Ordinal);
        await FormMissingRequestContractCapture.WriteAsync(list,"pending-" + culture,Ct);
        var body = await list.ReadJsonAsync(); var row = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(3,row.GetProperty("requestCount").GetInt32()); Assert.Equal(2,row.GetProperty("personCount").GetInt32());
        Assert.Equal(culture,row.GetProperty("originalCulture").GetString()); Assert.Equal(3,row.GetProperty("revision").GetInt64());
        Assert.Equal(3,body.GetProperty("totalRequests").GetInt32());
        var raw = await list.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Private requester",raw,StringComparison.Ordinal); Assert.DoesNotContain(scenario.SubjectId.ToString(),raw,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceReceiptId",raw,StringComparison.Ordinal); Assert.DoesNotContain("messages",raw,StringComparison.Ordinal);
        using var discarded = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{first.Value.TopicId}/discard",new { expectedRevision = 3 },Ct);
        Assert.Equal(HttpStatusCode.OK,discarded.StatusCode);
        var decision = await discarded.ReadJsonAsync(); Assert.Equal("Discarded",decision.GetProperty("state").GetString());
        Assert.Equal(scenario.ManagerId,decision.GetProperty("discardedBy").GetProperty("userId").GetGuid());
        using var discardedList = await manager.GetAsync("/api/forms/missing-requests?state=Discarded&search=" + scenario.Suffix,Ct);
        Assert.Equal(HttpStatusCode.OK,discardedList.StatusCode); await FormMissingRequestContractCapture.WriteAsync(discardedList,"discarded-" + culture,Ct);
        using var stale = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{first.Value.TopicId}/discard",new { expectedRevision = 3 },Ct);
        Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        if (culture == "es") await FormMissingRequestContractCapture.WriteAsync(stale,"conflict",Ct);
        var replay = await scenario.CaptureAsync(firstRequest,Ct); Assert.True(replay.IsSuccess); Assert.True(replay.Value.WasAlreadyRecorded);
        using var stillDiscarded = await manager.GetAsync("/api/forms/missing-requests?state=Discarded&search=" + scenario.Suffix,Ct);
        Assert.Single((await stillDiscarded.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        var reopened = await scenario.CaptureAsync(await scenario.PrepareAsync(existing:first.Value.TopicId,ct:Ct),Ct); Assert.True(reopened.IsSuccess);
        using var pending = await manager.GetAsync("/api/forms/missing-requests?search=" + scenario.Suffix,Ct);
        var reopenedRow = Assert.Single((await pending.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(4,reopenedRow.GetProperty("requestCount").GetInt32()); Assert.Equal(5,reopenedRow.GetProperty("revision").GetInt64());
        Assert.Equal(decision.GetProperty("discardedAtUtc").GetString(),reopenedRow.GetProperty("discardedAtUtc").GetString());
    }
    [Fact]
    public async Task Concurrent_replay_counts_one_message_and_http_never_accepts_requester_data()
    {
        var scenario = await FormMissingRequestScenario.CreateAsync(factory,"es",Ct); var request = await scenario.PrepareAsync(ct:Ct);
        var results = await Task.WhenAll(scenario.CaptureAsync(request,Ct),scenario.CaptureAsync(request,Ct));
        Assert.All(results,result => Assert.True(result.IsSuccess)); Assert.Equal(results[0].Value.RequestId,results[1].Value.RequestId);
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormMissingRequest>().AsNoTracking().Where(value => value.SourceReceiptId == request.SourceReceiptId).ToListAsync(Ct));
        Assert.Single(stored);
        using var anonymous = factory.CreateClient(); using var unsigned = await anonymous.GetAsync("/api/forms/missing-requests",Ct);
        Assert.Equal(HttpStatusCode.Unauthorized,unsigned.StatusCode);
        using var subject = scenario.Client(scenario.SubjectId); using var forbidden = await subject.GetAsync("/api/forms/missing-requests",Ct);
        Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode); await FormMissingRequestContractCapture.WriteAsync(forbidden,"forbidden",Ct);
        using var manager = scenario.Client(scenario.ManagerId);
        using var unknown = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{results[0].Value.TopicId}/discard",new { expectedRevision = 1, requesterName = "Private name" },Ct);
        Assert.Equal(HttpStatusCode.BadRequest,unknown.StatusCode);
        using var invalidPage = await manager.GetAsync("/api/forms/missing-requests?page=0",Ct); Assert.Equal(HttpStatusCode.BadRequest,invalidPage.StatusCode);
        using var invalidState = await manager.GetAsync("/api/forms/missing-requests?state=Opened",Ct); Assert.Equal(HttpStatusCode.BadRequest,invalidState.StatusCode);
    }
}
