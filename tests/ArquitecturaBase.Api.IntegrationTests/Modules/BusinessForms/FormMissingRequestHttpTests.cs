using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
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
        await using var scenario = await FormMissingRequestScenario.CreateAsync(factory,culture,Ct);
        var firstRequest = await scenario.PrepareAsync(ct:Ct);
        var first = await scenario.CaptureAsync(firstRequest,Ct); Assert.True(first.IsSuccess);
        await scenario.ReviewAsync(first.Value.TopicId,Ct);
        var samePerson = await scenario.CaptureAsync(await scenario.PrepareAsync(existing:first.Value.TopicId,ct:Ct),Ct); Assert.True(samePerson.IsSuccess);
        var otherPerson = await scenario.CaptureAsync(await scenario.PrepareAsync(scenario.OtherSubjectId,first.Value.TopicId,Ct),Ct); Assert.True(otherPerson.IsSuccess);
        using var manager = scenario.Client(scenario.ManagerId); manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var list = await manager.GetAsync("/api/forms/missing-requests?search=" + scenario.Suffix,Ct);
        Assert.Equal(HttpStatusCode.OK,list.StatusCode); Assert.Contains("no-store",list.Headers.CacheControl?.ToString() ?? "",StringComparison.Ordinal);
        await FormMissingRequestContractCapture.WriteAsync(list,"pending-" + culture,Ct);
        var body = await list.ReadJsonAsync(); var row = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(3,row.GetProperty("requestCount").GetInt32()); Assert.Equal(2,row.GetProperty("personCount").GetInt32());
        Assert.Equal(scenario.ManagerId,row.GetProperty("reviewedBy").GetProperty("userId").GetGuid());
        Assert.Equal(culture,row.GetProperty("originalCulture").GetString()); Assert.Equal(4,row.GetProperty("revision").GetInt64());
        Assert.Equal(3,body.GetProperty("totalRequests").GetInt32());
        var raw = await list.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Private requester",raw,StringComparison.Ordinal); Assert.DoesNotContain(scenario.SubjectId.ToString(),raw,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceReceiptId",raw,StringComparison.Ordinal); Assert.DoesNotContain("messages",raw,StringComparison.Ordinal);
        using var discarded = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{first.Value.TopicId}/discard",new { expectedRevision = 4 },Ct);
        Assert.Equal(HttpStatusCode.OK,discarded.StatusCode);
        var decision = await discarded.ReadJsonAsync(); Assert.Equal("Discarded",decision.GetProperty("state").GetString());
        Assert.Equal(scenario.ManagerId,decision.GetProperty("discardedBy").GetProperty("userId").GetGuid());
        using var discardedList = await manager.GetAsync("/api/forms/missing-requests?state=Discarded&search=" + scenario.Suffix,Ct);
        Assert.Equal(HttpStatusCode.OK,discardedList.StatusCode); await FormMissingRequestContractCapture.WriteAsync(discardedList,"discarded-" + culture,Ct);
        using var stale = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{first.Value.TopicId}/discard",new { expectedRevision = 4 },Ct);
        Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        if (culture == "es") await FormMissingRequestContractCapture.WriteAsync(stale,"conflict",Ct);
        var replay = await scenario.CaptureAsync(firstRequest,Ct); Assert.True(replay.IsSuccess); Assert.True(replay.Value.WasAlreadyRecorded);
        using var stillDiscarded = await manager.GetAsync("/api/forms/missing-requests?state=Discarded&search=" + scenario.Suffix,Ct);
        Assert.Single((await stillDiscarded.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        var reopened = await scenario.CaptureAsync(await scenario.PrepareAsync(ct:Ct),Ct); Assert.True(reopened.IsSuccess);
        using var pending = await manager.GetAsync("/api/forms/missing-requests?search=" + scenario.Suffix,Ct);
        var reopenedRow = Assert.Single((await pending.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(4,reopenedRow.GetProperty("requestCount").GetInt32()); Assert.Equal(6,reopenedRow.GetProperty("revision").GetInt64());
        Assert.Equal(JsonValueKind.Null,reopenedRow.GetProperty("reviewedAtUtc").ValueKind);
        Assert.Equal(decision.GetProperty("discardedAtUtc").GetString(),reopenedRow.GetProperty("discardedAtUtc").GetString());
    }
    [Fact]
    public async Task A_topic_is_offered_to_the_assistant_only_after_it_is_reviewed_and_review_answers_409_403_and_404()
    {
        await using var scenario = await FormMissingRequestScenario.CreateAsync(factory,"es",Ct);
        var first = await scenario.CaptureAsync(await scenario.PrepareAsync(ct:Ct),Ct); Assert.True(first.IsSuccess);
        var topicId = first.Value.TopicId;
        Assert.Empty(await scenario.ScopeAsync(services => services.GetRequiredService<IFormMissingRequestReader>().ListAssistantTopicsAsync(Ct)));
        var duplicate = await scenario.CaptureAsync(await scenario.PrepareAsync(scenario.OtherSubjectId,ct:Ct),Ct);
        Assert.True(duplicate.IsSuccess); Assert.Equal(topicId,duplicate.Value.TopicId);
        using var subject = scenario.Client(scenario.SubjectId);
        using var forbidden = await subject.PostAsJsonAsync($"/api/forms/missing-requests/{topicId}/review",new { expectedRevision = 2 },Ct);
        Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        using var manager = scenario.Client(scenario.ManagerId);
        using var missing = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{Guid.NewGuid()}/review",new { expectedRevision = 1 },Ct);
        Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        using var old = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{topicId}/review",new { expectedRevision = 1 },Ct);
        Assert.Equal(HttpStatusCode.Conflict,old.StatusCode);
        Assert.Empty(await scenario.ScopeAsync(services => services.GetRequiredService<IFormMissingRequestReader>().ListAssistantTopicsAsync(Ct)));
        using var reviewed = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{topicId}/review",new { expectedRevision = 2 },Ct);
        Assert.Equal(HttpStatusCode.OK,reviewed.StatusCode);
        Assert.Equal(scenario.ManagerId,(await reviewed.ReadJsonAsync()).GetProperty("reviewedBy").GetProperty("userId").GetGuid());
        Assert.Equal(topicId,Assert.Single(await scenario.ScopeAsync(services => services.GetRequiredService<IFormMissingRequestReader>().ListAssistantTopicsAsync(Ct))).Id);
        using var again = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{topicId}/review",new { expectedRevision = 3 },Ct);
        Assert.Equal(HttpStatusCode.Conflict,again.StatusCode);
    }
    [Fact]
    public async Task Independent_missing_request_scenarios_never_observe_each_others_topics()
    {
        await using var first=await FormMissingRequestScenario.CreateAsync(factory,"es",Ct);
        Assert.True((await first.CaptureAsync(await first.PrepareAsync(ct:Ct),Ct)).IsSuccess);
        await using var second=await FormMissingRequestScenario.CreateAsync(factory,"es",Ct);
        var request=await second.PrepareAsync(ct:Ct);
        var read=Assert.IsType<ArquitecturaBase.Application.Modules.BusinessForms.Models.FormAssistantCandidatesPayload>(Assert.Single(request.Proposal.ObservedReads).Payload);
        Assert.True(read.IsComplete);Assert.Empty(read.Topics);
    }
    [Fact]
    public async Task Concurrent_replay_counts_one_message_and_http_never_accepts_requester_data()
    {
        await using var scenario = await FormMissingRequestScenario.CreateAsync(factory,"es",Ct); var request = await scenario.PrepareAsync(ct:Ct);
        var results = await Task.WhenAll(scenario.CaptureAsync(request,Ct),scenario.CaptureAsync(request,Ct));
        Assert.All(results,result => Assert.True(result.IsSuccess)); Assert.Equal(results[0].Value.RequestId,results[1].Value.RequestId);
        var stored = await scenario.ScopeAsync(services=>services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().Set<BusinessFormMissingRequest>().AsNoTracking().Where(value=>value.SourceReceiptId==request.SourceReceiptId).ToListAsync(Ct));
        Assert.Single(stored);
        using var anonymous = scenario.Client(); using var unsigned = await anonymous.GetAsync("/api/forms/missing-requests",Ct);
        Assert.Equal(HttpStatusCode.Unauthorized,unsigned.StatusCode);
        using var subject = scenario.Client(scenario.SubjectId); using var forbidden = await subject.GetAsync("/api/forms/missing-requests",Ct);
        Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode); await FormMissingRequestContractCapture.WriteAsync(forbidden,"forbidden",Ct);
        using var manager = scenario.Client(scenario.ManagerId);
        using var unknown = await manager.PostAsJsonAsync($"/api/forms/missing-requests/{results[0].Value.TopicId}/discard",new { expectedRevision = 1, requesterName = "Private name" },Ct);
        Assert.Equal(HttpStatusCode.BadRequest,unknown.StatusCode);
        using var invalidPage = await manager.GetAsync("/api/forms/missing-requests?page=0",Ct); Assert.Equal(HttpStatusCode.BadRequest,invalidPage.StatusCode);
        using var invalidState = await manager.GetAsync("/api/forms/missing-requests?state=Opened",Ct); Assert.Equal(HttpStatusCode.BadRequest,invalidState.StatusCode);
    }
    // OT1-C: the board sorts by every column with a datum in PostgreSQL. Equal values keep the usual order, the last request first; who discarded
    // a topic goes by the name of their account, and an account that is gone (the table shows «—») goes last also descending.
    [Fact]
    public async Task The_board_sorts_by_its_columns_with_a_discard_by_an_account_that_is_gone_last()
    {
        await using var scenario = await FormMissingRequestScenario.CreateAsync(factory,"es",Ct);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            BusinessFormMissingTopic Topic(string name,params (Guid Subject,TimeSpan Ago)[] requests)
            {
                var topic = BusinessFormMissingTopic.Create(name + " " + scenario.Suffix,"Fictitious request.","es").Value;
                db.Set<BusinessFormMissingTopic>().Add(topic);
                foreach (var (person,ago) in requests)
                    db.Set<BusinessFormMissingRequest>().Add(BusinessFormMissingRequest.Create(Guid.NewGuid(),topic.Id,person,now - ago).Value);
                return topic;
            }
            var (subject,other) = (scenario.SubjectId,scenario.OtherSubjectId);
            Topic("Alfa",(subject,TimeSpan.FromHours(3)));
            Topic("Beta",(subject,TimeSpan.FromHours(5)),(subject,TimeSpan.FromHours(4)),(other,TimeSpan.FromHours(1)));
            Topic("Gama",(subject,TimeSpan.FromHours(2)),(subject,TimeSpan.FromMinutes(119)));
            // Discarded by «Missing reviewer …», by «Private requester …» and by an account that is gone.
            Assert.True(Topic("Delta",(subject,TimeSpan.FromHours(6))).Discard(1,scenario.ManagerId,now.AddMinutes(-30)).IsSuccess);
            Assert.True(Topic("Epsilon",(subject,TimeSpan.FromHours(7))).Discard(1,subject,now.AddMinutes(-20)).IsSuccess);
            Assert.True(Topic("Zeta",(subject,TimeSpan.FromHours(8))).Discard(1,Guid.CreateVersion7(),now.AddMinutes(-10)).IsSuccess);
            return await db.SaveChangesAsync(Ct);
        });
        using var manager = scenario.Client(scenario.ManagerId);
        async Task<string[]> Sorted(string sort,string state = "Pending")
        {
            using var response = await manager.GetAsync($"/api/forms/missing-requests?state={state}&search={scenario.Suffix}&sort={sort}",Ct);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            return [.. (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!.Split(' ')[0])];
        }

        Assert.Equal(["Alfa","Beta","Gama"],await Sorted("name"));
        Assert.Equal(["Gama","Beta","Alfa"],await Sorted("-name"));
        Assert.Equal(["Alfa","Gama","Beta"],await Sorted("requestCount"));
        Assert.Equal(["Beta","Gama","Alfa"],await Sorted("-requestCount"));
        // Alfa and Gama have one person each: the last request decides.
        Assert.Equal(["Gama","Alfa","Beta"],await Sorted("personCount"));
        Assert.Equal(["Beta","Gama","Alfa"],await Sorted("-personCount"));
        Assert.Equal(["Alfa","Gama","Beta"],await Sorted("lastRequestedAtUtc"));
        Assert.Equal(["Delta","Epsilon","Zeta"],await Sorted("discardedBy","Discarded"));
        Assert.Equal(["Epsilon","Delta","Zeta"],await Sorted("-discardedBy","Discarded"));
        Assert.Equal(["Delta","Epsilon","Zeta"],await Sorted("discardedAtUtc","Discarded"));
        Assert.Equal(["Zeta","Epsilon","Delta"],await Sorted("-discardedAtUtc","Discarded"));
    }
}
