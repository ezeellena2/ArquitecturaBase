using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Suggested_capture_keeps_real_lineage_mapping_changes_and_historical_mode_in_postgres_and_http()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var recipient = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        using var other = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.SuggestionsManage]);
        var recipientId = Guid.Parse(recipient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var otherId = Guid.Parse(other.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var operatorId = Guid.Parse(manager.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        await using var host = PublicationHost(); using var publisher = host.CreateClient();
        publisher.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var source = await PublishSuggestionFormAsync(host, publisher, "Entrega histórica");
        var target = await PublishSuggestionFormAsync(host, publisher, "Factura histórica");
        var sourceField = source.Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0];
        var targetField = target.Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0];
        var sourceKey = sourceField.GetProperty("key").GetString()!;
        var targetKey = targetField.GetProperty("key").GetString()!;
        var targetFieldId = targetField.GetProperty("fieldId").GetGuid();
        var suggestionId = Guid.NewGuid(); var route = $"/api/forms/definitions/{source.Id}/suggestions";
        using var configured = await manager.PostAsJsonAsync(route, new { expectedRevision = 0, suggestions = new[] {
            new { id = suggestionId, destinationDefinitionId = target.Id, mode = "Always", when = (string?)null, isActive = true,
                conditions = Array.Empty<object>(), mappings = new[] { new { source = new { kind = "RecordNumber" }, destinationFieldId = targetFieldId } } } } }, Ct);
        Assert.Equal(HttpStatusCode.Created, configured.StatusCode);
        var setId = (await configured.ReadJsonAsync()).GetProperty("id").GetGuid();
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        Guid sourceRecordId = Guid.Empty; Guid firstChild = Guid.Empty; Guid secondChild = Guid.Empty;
        await factory.ExecuteScopeAsync(async services =>
        {
            var sink = services.GetRequiredService<IStructuredRecordSink>();
            var forms = services.GetRequiredService<IStructuredFormSource>();
            var captured = await sink.CaptureAsync(new(Guid.NewGuid(), source.Id, source.VersionId, recipientId, now,
                new Dictionary<string, string> { [sourceKey] = "Entrega confirmada" }), Ct);
            Assert.Equal("Completed", captured.State); Assert.NotNull(captured.RecordId); sourceRecordId = captured.RecordId.Value;
            Assert.True((await forms.ListSuggestionsAsync(otherId, "es", sourceRecordId, Ct)).IsFailure);
            var choices = await forms.ListSuggestionsAsync(recipientId, "es", sourceRecordId, Ct);
            Assert.True(choices.IsSuccess); Assert.Equal("Always", Assert.Single(choices.Value.Candidates).Mode);
            var prepared = await forms.PrepareSuggestionAsync(new(recipientId, "es", sourceRecordId, setId, suggestionId), Ct);
            Assert.True(prepared.IsSuccess); Assert.Equal("es", prepared.Value.Prefill.Culture);
            var proposed = Assert.Single(prepared.Value.ProposedFields);
            var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual, operatorId, prepared.Value.Prefill.Reason, null,
                [new(proposed.FieldId, proposed.Type, proposed.Value)], [proposed.FieldId], prepared.Value.OriginSuggestion);
            var child = new StructuredRecordCaptureRequest(Guid.NewGuid(), target.Id, target.VersionId, recipientId, now.AddMinutes(1),
                new Dictionary<string, string> { [targetKey] = "Corregido por la persona" }, Origin: origin);
            var first = await sink.CaptureAsync(child, Ct); var replay = await sink.CaptureAsync(child, Ct);
            Assert.Equal("Completed", first.State); Assert.Equal(first, replay); Assert.NotNull(first.RecordId); firstChild = first.RecordId.Value;
            var second = await sink.CaptureAsync(child with { SourceReceiptId = Guid.NewGuid(), SubmittedAtUtc = now.AddMinutes(2) }, Ct);
            Assert.Equal("Completed", second.State); Assert.NotNull(second.RecordId); secondChild = second.RecordId.Value;
            var responseReader = services.GetRequiredService<IFormResponseReader>();
            Assert.Equal(new[] { source.Id, target.Id }.Order(), (await responseReader.ListAncestorDefinitionIdsAsync(firstChild, Ct)).Order());
            Assert.Equal(new[] { sourceRecordId, firstChild, secondChild }, (await responseReader.ListChainAsync(firstChild, Ct)).Select(row => row.Id));
            Assert.Equal(new[] { sourceRecordId, firstChild, secondChild }, (await responseReader.ListChainAsync(sourceRecordId, Ct)).Select(row => row.Id));
        });
        using var changed = await manager.PostAsJsonAsync(route, new { expectedRevision = 1, suggestions = new[] {
            new { id = suggestionId, destinationDefinitionId = target.Id, mode = "Assistant", when = "Cuando pida seguimiento", isActive = true,
                conditions = Array.Empty<object>(), mappings = new[] { new { source = new { kind = "RecordNumber" }, destinationFieldId = targetFieldId } } } } }, Ct);
        Assert.Equal(HttpStatusCode.Created, changed.StatusCode);
        foreach (var culture in new[] { "es", "en" })
        {
            recipient.DefaultRequestHeaders.AcceptLanguage.Clear(); recipient.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var detail = await recipient.GetAsync($"/api/forms/records/{firstChild}", Ct);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var body = await detail.ReadJsonAsync(); var assistance = body.GetProperty("assistance"); var value = Assert.Single(assistance.GetProperty("values").EnumerateArray());
            Assert.Equal("Manual", assistance.GetProperty("senderKind").GetString());
            Assert.Equal(operatorId, assistance.GetProperty("operator").GetProperty("userId").GetGuid());
            Assert.Equal("Always", assistance.GetProperty("suggestion").GetProperty("mode").GetString());
            Assert.Equal(sourceRecordId, assistance.GetProperty("suggestion").GetProperty("originRecordId").GetGuid());
            Assert.True(value.GetProperty("included").GetBoolean()); Assert.True(value.GetProperty("changed").GetBoolean()); Assert.True(value.GetProperty("mapped").GetBoolean());
            Assert.Equal(new[] { sourceRecordId, firstChild, secondChild }, body.GetProperty("chain").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));
            Assert.DoesNotContain("proposalId", body.GetRawText(), StringComparison.Ordinal); Assert.DoesNotContain("suggestionSetId", body.GetRawText(), StringComparison.Ordinal);
            await FormSuggestionDetailContractCapture.WriteAsync(detail, "detail-" + culture, Ct);
        }
        await factory.ExecuteDbContextAsync(async db =>
        {
            var child = await db.Set<BusinessFormRecord>().SingleAsync(row => row.Id == firstChild, Ct);
            Assert.Equal(sourceRecordId, child.OriginRecordId); Assert.Equal(suggestionId, child.OriginSuggestionId); Assert.NotNull(child.CaptureOriginJson);
            Assert.Equal(2, await db.Set<BusinessFormRecord>().CountAsync(row => row.OriginRecordId == sourceRecordId, Ct));
        });
    }
}
