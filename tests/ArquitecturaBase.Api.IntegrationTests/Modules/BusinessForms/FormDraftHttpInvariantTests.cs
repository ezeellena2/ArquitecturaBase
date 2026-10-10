using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_prefix_uniqueness_preserves_one_definition_and_no_receipt_for_the_loser(bool concurrent)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        await using var host = factory.WithWebHostBuilder(_ => { });
        using var other = host.CreateClient();
        other.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var first = Payload("Prefix first");
        var second = Payload("Prefix second");
        var prefix = first["prefix"]!.GetValue<string>().ToUpperInvariant();
        first["prefix"] = prefix;
        second["prefix"] = prefix;
        HttpResponseMessage[] responses;
        if (concurrent)
        {
            await using var connection = new NpgsqlConnection(factory.ConnectionString);
            await connection.OpenAsync(Ct);
            await using var held = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended('forms:definitions', 0))", connection);
            await held.ExecuteNonQueryAsync(Ct);
            var left = actor.PostAsJsonAsync("/api/forms/definitions", first, Ct);
            var right = other.PostAsJsonAsync("/api/forms/definitions", second, Ct);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await using var waiting = new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted", connection);
                while ((long)(await waiting.ExecuteScalarAsync(timeout.Token))! < 2)
                    await Task.Delay(10, timeout.Token);
                Assert.False(left.IsCompleted);
                Assert.False(right.IsCompleted);
            }
            finally
            {
                await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended('forms:definitions', 0))", connection);
                await release.ExecuteNonQueryAsync(Ct);
            }
            responses = await Task.WhenAll(left, right);
        }
        else
        {
            responses = [await actor.PostAsJsonAsync("/api/forms/definitions", first, Ct), await other.PostAsJsonAsync("/api/forms/definitions", second, Ct)];
        }
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest }, responses.Select(response => response.StatusCode).Order());
        var loserIndex = firstResponse.StatusCode == HttpStatusCode.BadRequest ? 0 : 1;
        var problem = await responses[loserIndex].ReadJsonAsync();
        Assert.Equal("Forms.Definition.PrefixTaken", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("prefix", out _));
        var loser = loserIndex == 0 ? first : second;
        using var missingReceipt = await actor.GetAsync("/api/forms/draft-changes/" + loser["requestId"]!.GetValue<string>(), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missingReceipt.StatusCode);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().CountAsync(value => value.Prefix == prefix, Ct)));
    }

    [Fact]
    public async Task Http_prefix_changes_are_rejected_after_acceptance_without_saving_a_new_revision()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(author, Payload("Locked prefix"));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost();
        using var publisher = host.CreateClient();
        publisher.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(publisher, id, saved);
        using var accepted = await publisher.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var edit = EditPayload(saved);
        edit["prefix"] = "P" + Guid.NewGuid().ToString("N")[..10];
        using var rejected = await author.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("Forms.Definition.PrefixLocked", (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
        await AssertDraftUnchangedAsync(author, id, saved, edit);
    }

    [Theory]
    [InlineData("culture", "draft.culture")]
    [InlineData("change-type", "draft.fields[0].type")]
    [InlineData("same-type-replacement", "draft.fields[0].type")]
    [InlineData("same-question-twice", "draft.fields[1].fieldId")]
    [InlineData("two-identities", "draft.fields[0].fieldId")]
    public async Task Http_edits_reject_culture_and_question_identity_changes_without_a_receipt(string scenario, string path)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var saved = await CreateAsync(actor, Payload("Identity invariants"));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var edit = EditPayload(saved);
        var field = edit["draft"]!["fields"]![0]!.AsObject();
        switch (scenario)
        {
            case "culture": edit["draft"]!["culture"] = "en"; break;
            case "change-type": field["type"] = "longText"; break;
            case "same-type-replacement": field["replacesFieldId"] = field["fieldId"]!.DeepClone(); field.Remove("fieldId"); break;
            case "same-question-twice":
                var duplicate = field.DeepClone().AsObject(); duplicate["localId"] = "duplicate";
                edit["draft"]!["fields"]!.AsArray().Add(duplicate); break;
            default: field["replacesFieldId"] = field["fieldId"]!.DeepClone(); break;
        }
        using var rejected = await actor.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.True((await rejected.ReadJsonAsync()).GetProperty("errors").TryGetProperty(path, out _));
        await AssertDraftUnchangedAsync(actor, id, saved, edit);
    }

    [Theory]
    [InlineData("fieldId", "draft.fields[0].fieldId")]
    [InlineData("replacesFieldId", "draft.fields[0].replacesFieldId")]
    [InlineData("choiceId", "draft.fields[0].options[0].choiceId")]
    public async Task Http_creation_does_not_accept_question_or_option_identities_from_the_client(string property, string path)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var payload = Payload("Foreign creation identity");
        var field = payload["draft"]!["fields"]![0]!.AsObject();
        if (property == "choiceId")
        {
            field["type"] = "choice";
            field["options"] = new JsonArray(new JsonObject { ["localId"] = "option", ["choiceId"] = Guid.NewGuid().ToString(), ["label"] = new JsonObject { ["es"] = "Una", ["en"] = "One" } });
        }
        else field[property] = Guid.NewGuid().ToString();
        using var rejected = await actor.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.True((await rejected.ReadJsonAsync()).GetProperty("errors").TryGetProperty(path, out _));
        using var receipt = await actor.GetAsync("/api/forms/draft-changes/" + payload["requestId"]!.GetValue<string>(), Ct);
        Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
    }

    [Fact]
    public async Task Http_update_to_an_unknown_definition_is_not_found_and_does_not_create_a_receipt()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var payload = Payload("Unknown definition");
        payload["expectedRevision"] = 1;
        using var rejected = await actor.PutAsJsonAsync($"/api/forms/definitions/{Guid.NewGuid()}/draft", payload, Ct);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        Assert.Equal("Forms.Definition.NotFound", (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
        using var receipt = await actor.GetAsync("/api/forms/draft-changes/" + payload["requestId"]!.GetValue<string>(), Ct);
        Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
    }

    [Theory]
    [InlineData("shortText", 0, false)]
    [InlineData("shortText", 10001, false)]
    [InlineData("longText", 0, false)]
    [InlineData("longText", 10001, false)]
    [InlineData("decimal", 1, false)]
    [InlineData("shortText", 1, true)]
    [InlineData("shortText", 10000, true)]
    [InlineData("longText", 1, true)]
    [InlineData("longText", 10000, true)]
    public async Task Http_draft_max_length_limits_are_enforced_for_the_question_type(string type, int length, bool valid)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var payload = Payload("Length boundary");
        payload["draft"]!["fields"]![0]!["type"] = type;
        payload["draft"]!["fields"]![0]!["maxLength"] = length;
        using var response = await actor.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.ReadJsonAsync();
        if (valid) Assert.Equal(length, body.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("maxLength").GetInt32());
        else Assert.True(body.GetProperty("errors").TryGetProperty("draft.fields[0].maxLength", out _));
    }

    [Theory]
    [InlineData(40, 50, true)]
    [InlineData(41, 1, false)]
    [InlineData(1, 51, false)]
    [InlineData(1, 0, false)]
    public async Task Http_draft_question_and_choice_count_limits_are_observed_in_persisted_responses(int questionCount, int choiceCount, bool valid)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var payload = Payload("Question and choice boundaries");
        var fields = new JsonArray();
        for (var i = 0; i < questionCount; i++)
        {
            var choices = new JsonArray();
            for (var j = 0; j < choiceCount; j++) choices.Add(new JsonObject { ["localId"] = "o" + j, ["label"] = new JsonObject { ["es"] = "Opción " + j, ["en"] = "Option " + j } });
            fields.Add(new JsonObject { ["localId"] = "f" + i, ["type"] = "choice", ["label"] = new JsonObject { ["es"] = "Pregunta " + i, ["en"] = "Question " + i }, ["options"] = choices });
        }
        payload["draft"]!["fields"] = fields;
        using var response = await actor.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.ReadJsonAsync();
        if (valid)
        {
            var stored = body.GetProperty("definition").GetProperty("draft").GetProperty("fields").EnumerateArray().ToArray();
            Assert.Equal(questionCount, stored.Length);
            Assert.All(stored, field => Assert.Equal(choiceCount, field.GetProperty("options").GetArrayLength()));
        }
        else Assert.True(body.GetProperty("errors").TryGetProperty(questionCount > 40 ? "draft.fields" : "draft.fields[0].options", out _));
    }

    private static async Task AssertDraftUnchangedAsync(HttpClient actor, Guid id, JsonElement saved, JsonObject attempt)
    {
        using var detail = await actor.GetAsync($"/api/forms/definitions/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(saved.GetProperty("definition").GetProperty("revision").GetInt64(), (await detail.ReadJsonAsync()).GetProperty("definition").GetProperty("revision").GetInt64());
        using var missingReceipt = await actor.GetAsync("/api/forms/draft-changes/" + attempt["requestId"]!.GetValue<string>(), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missingReceipt.StatusCode);
    }
}
