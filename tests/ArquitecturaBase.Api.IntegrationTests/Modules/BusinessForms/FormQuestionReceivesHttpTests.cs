using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// W3 (UC1-A) with PostgreSQL: each text question saves what it receives when the person shares a location or a contact by a channel.
// It comes back in the draft and the detail, the app's WhatsApp channel checks the form without an issue, the publication snapshot
// carries it to the channel and the version seals it; a question without it keeps its bytes. A question that cannot receive it is
// refused at its path with its text. The intact draft and detail go to forms-http/location-contact.
public sealed partial class FormDraftEndpointsTests
{
    private static JsonObject ReceivingQuestion(string localId, string type, string spanish, string english, string? receives = null)
    {
        var question = new JsonObject { ["localId"] = localId, ["type"] = type, ["label"] = new JsonObject { ["es"] = spanish, ["en"] = english } };
        if (receives is not null) question["receives"] = receives;
        return question;
    }

    [Fact]
    public async Task What_a_text_question_receives_by_a_channel_is_saved_published_and_sealed_and_a_question_without_it_keeps_its_bytes()
    {
        using var writer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        writer.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, writer.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var form = Payload("Entregas " + Guid.NewGuid().ToString("N")[..10]);
        var fields = form["draft"]!["fields"]!.AsArray();
        fields.Add(ReceivingQuestion("address", "shortText", "Dirección de entrega", "Delivery address", "location"));
        fields.Add(ReceivingQuestion("receiver", "longText", "Recibió", "Received by", "contactName"));
        fields.Add(ReceivingQuestion("phone", "shortText", "Teléfono", "Phone", "contactPhone"));
        using var created = await client.PostAsJsonAsync("/api/forms/definitions", form, Ct);
        var saved = await CaptureContractAsync(created, HttpStatusCode.Created, "location-contact", "draft-saved");
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        string?[] expected = [null, "location", "contactName", "contactPhone"];
        Assert.Equal(expected, Received(saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")));
        using (var stored = JsonDocument.Parse(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == id)
            .Select(value => value.SchemaJson).SingleAsync(Ct))))
            Assert.Equal(expected, Received(stored.RootElement.GetProperty("fields")));

        // The app's WhatsApp channel checks the draft with it and reports no issue (accepting needs Meta).
        using var compiled = await writer.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, compiled.StatusCode);
        var compilation = await compiled.ReadJsonAsync();
        Assert.Empty(compilation.GetProperty("issues").EnumerateArray());
        Assert.NotEmpty(compilation.GetProperty("channels").EnumerateArray());

        // The publication snapshot carries it to the channel, and the version seals it.
        var attempt = await PublicationAttemptAsync(client, id, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var operationId = (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid();
        Guid versionId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var dispatch = scope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
            var work = (await dispatch.ReserveAsync(operationId, Ct)).Value!;
            var snapshot = Assert.Single(work.ChannelRequests).Request.Snapshot;
            Assert.Equal(expected, snapshot.Questions.Select(question => question.Receives));
            var published = new StructuredFormPublicationResponse(work.OperationId, id, work.VersionId,
                work.Manifest.PublicationFingerprint, "Published", "Verify", [], null, false);
            Assert.True((await dispatch.CompleteAsync(work, [new("test", published)], Ct)).IsSuccess);
            versionId = work.VersionId;
        }
        using (var schema = JsonDocument.Parse(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().Where(value => value.Id == versionId)
            .Select(value => value.SchemaJson).SingleAsync(Ct))))
            Assert.Equal(expected, Received(schema.RootElement.GetProperty("questions")));

        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        var loaded = await CaptureContractAsync(detail, HttpStatusCode.OK, "location-contact", "detail-es");
        Assert.Equal(versionId, loaded.GetProperty("publication").GetProperty("activeVersion").GetProperty("versionId").GetGuid());
        Assert.Equal(expected, Received(loaded.GetProperty("definition").GetProperty("draft").GetProperty("fields")));
    }

    [Fact]
    public async Task A_question_that_cannot_receive_what_the_person_shares_is_refused_at_its_path()
    {
        using var client = await AdminAsync();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var form = Payload("Entregas " + Guid.NewGuid().ToString("N")[..10]);
        var fields = form["draft"]!["fields"]!.AsArray();
        fields.Add(ReceivingQuestion("weight", "decimal", "Peso", "Weight", "contactPhone"));
        fields.Add(ReceivingQuestion("receiver", "shortText", "Recibió", "Received by", "contactName"));
        fields.Add(ReceivingQuestion("signer", "longText", "Firmó", "Signed by", "contactName"));
        fields.Add(ReceivingQuestion("point", "location", "Ubicación", "Location"));
        fields.Add(ReceivingQuestion("address", "shortText", "Dirección", "Address", "location"));
        // A question with a condition is never prefilled by the Flow (CO1-A).
        var delivered = Guid.NewGuid().ToString("D");
        fields.Add(ReceivingQuestion(delivered, "boolean", "¿Entregado?", "Delivered?"));
        var conditional = ReceivingQuestion("phone", "shortText", "Teléfono", "Phone", "contactPhone");
        conditional["showWhen"] = new JsonObject { ["questionId"] = delivered, ["values"] = new JsonArray("true") };
        fields.Add(conditional);
        // Nor one with a fixed validation (DOC1).
        var taxId = ReceivingQuestion("tax", "shortText", "CUIT", "Tax ID", "contactPhone");
        taxId["validation"] = "taxId";
        fields.Add(taxId);

        using var response = await client.PostAsJsonAsync("/api/forms/definitions", form, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.Equal(["draft.fields[1].receives", "draft.fields[3].receives", "draft.fields[5].receives", "draft.fields[7].receives", "draft.fields[8].receives"],
            errors.EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal));
        const string onlyText = "Sólo una pregunta de texto, fuera del bloque, sin condición ni validación fija, puede recibir la ubicación o el contacto.";
        Assert.Equal(onlyText, errors.GetProperty("draft.fields[1].receives")[0].GetString());
        Assert.Equal("Otra pregunta ya recibe ese dato.", errors.GetProperty("draft.fields[3].receives")[0].GetString());
        Assert.Equal("El formulario tiene una pregunta «Ubicación»: la ubicación va ahí.", errors.GetProperty("draft.fields[5].receives")[0].GetString());
        Assert.Equal(onlyText, errors.GetProperty("draft.fields[7].receives")[0].GetString());
        Assert.Equal(onlyText, errors.GetProperty("draft.fields[8].receives")[0].GetString());
    }

    // What each question receives, null where the key is absent; a key with a null value would not be the same bytes.
    private static string?[] Received(JsonElement questions) => questions.EnumerateArray()
        .Select(question => question.TryGetProperty("receives", out var receives) ? receives.GetString() ?? "null" : null).ToArray();
}
