using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Real_multiple_capture_preserves_received_order_typed_references_and_replay_after_the_catalog_changes(string culture)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead,
            BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.RulesManage]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var suffix = Guid.NewGuid().ToString("N");
        var labels = new[] { (Es: "Primera", En: "First"), (Es: "Segunda", En: "Second"), (Es: "Tercera", En: "Third") };
        var canonical = new[] { "0.1234567890123456789012345678", "-1500.5", "0" };
        object CatalogItem(int index, Guid? id, bool active, bool changed) => new
        {
            localId = "item-" + index, itemId = id, code = "LEVEL" + index,
            text = new { es = changed ? "Nueva " + index : labels[index].Es, en = changed ? "Changed " + index : labels[index].En },
            detail = new { es = changed ? "Detalle nuevo" : "Detalle capturado", en = changed ? "Changed detail" : "Captured detail" },
            value = changed ? "7.5" : canonical[index], isActive = active,
        };
        using var created = await actor.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Niveles " + suffix, en = "Levels " + suffix },
            description = new { es = "Niveles", en = "Levels" }, valueType = "Decimal",
            items = Enumerable.Range(0, 3).Select(index => CatalogItem(index, null, true, false)).ToArray(),
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync(); var catalogId = saved.GetProperty("catalog").GetProperty("id").GetGuid();
        var itemIds = Enumerable.Range(0, 3).Select(index => saved.GetProperty("itemIdentityMap").GetProperty("item-" + index).GetGuid()).ToArray();
        var catalogField = MultipleCaptureField(true, null) with { Source = new(catalogId), Selection = new(2, 2) };
        var ownIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var ownField = MultipleCaptureField(false, ownIds) with
        { Label = new Dictionary<string, string> { ["es"] = "Opciones propias", ["en"] = "Own choices" } };
        var seeded = await SeedSheetAsync(actor, 0, catalogField, ownField);
        var versionId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>()
            .Where(definition => definition.Id == seeded.Id).Select(definition => definition.ActiveVersionId!.Value).SingleAsync(Ct));
        var ruleId = Guid.NewGuid(); var actionId = Guid.NewGuid();
        var rule = new FormRule(ruleId, "Received",
            [new(catalogField.FieldId, "anyOf", [itemIds[2].ToString("D"), itemIds[0].ToString("D")])],
            [new(actionId, "NotifyAuthor", null, new Dictionary<string, string> { ["es"] = "Respuesta capturada", ["en"] = "Response captured" }, false)]);
        using var ruleSaved = await actor.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/rules", new { expectedRevision = 0, rules = new[] { rule } }, Ct);
        Assert.Equal(HttpStatusCode.Created, ruleSaved.StatusCode);
        var preparation = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IStructuredFormSource>()
            .PrepareAsync(actorId, culture, seeded.Id, versionId, false, Ct));
        Assert.True(preparation.CanSend); Assert.Equal(culture, preparation.Culture);
        var offer = Assert.Single(preparation.Catalogs);
        Assert.Equal(itemIds, offer.Options.Select(option => option.Id));
        var selected = new[] { itemIds[2], itemIds[0] }; var ownSelected = new[] { ownIds[1], ownIds[0] };
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), seeded.Id, versionId, actorId,
            factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string>
            {
                [catalogField.Key] = JsonSerializer.Serialize(selected), [ownField.Key] = JsonSerializer.Serialize(ownSelected),
            }, [offer]) { PresentationCulture = culture };
        var first = await CaptureMultipleAsync(request);
        Assert.Equal("Completed", first.State); Assert.Equal(1, first.Number); Assert.NotNull(first.RecordId);
        Assert.Equal(first, await CaptureMultipleAsync(request));
        var conflict = await CaptureMultipleAsync(request with
        {
            Answers = new Dictionary<string, string>(request.Answers) { [catalogField.Key] = JsonSerializer.Serialize(selected.Reverse()) },
        });
        Assert.Equal("Rejected", conflict.State); Assert.Equal(FormRecordErrors.ReplayConflict.Code, conflict.ErrorCode);
        var record = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().SingleAsync(value => value.Id == first.RecordId, Ct));
        using (var json = JsonDocument.Parse(record.ValuesJson))
        {
            Assert.Equal(2, json.RootElement.GetArrayLength());
            foreach (var (field, expected) in new[] { (catalogField, selected), (ownField, ownSelected) })
            {
                var value = Assert.Single(json.RootElement.EnumerateArray(), value => value.GetProperty("fieldId").GetGuid() == field.FieldId).GetProperty("value");
                Assert.Equal(JsonValueKind.Array, value.ValueKind); Assert.Equal(expected, value.EnumerateArray().Select(item => item.GetGuid()));
            }
        }
        var values = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordValue>()
            .Where(value => value.RecordId == first.RecordId).OrderBy(value => value.Position).ToArrayAsync(Ct));
        Assert.Equal(4, values.Length);
        foreach (var (field, expected) in new[] { (catalogField, selected), (ownField, ownSelected) })
        {
            var choices = values.Where(value => value.FieldId == field.FieldId).ToArray();
            Assert.Equal(new[] { 0, 1 }, choices.Select(value => value.Position));
            Assert.Equal(expected, choices.Select(value => value.ChoiceId!.Value));
            Assert.All(choices, value => Assert.Equal("multiChoice", value.Type));
        }
        var references = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>()
            .Where(reference => reference.RecordId == first.RecordId).ToArrayAsync(Ct));
        Assert.Equal(2, references.Length);
        foreach (var id in selected)
        {
            var reference = Assert.Single(references, reference => reference.ItemId == id);
            var offered = Assert.Single(offer.Options, option => option.Id == id);
            Assert.Equal(catalogField.FieldId, reference.FieldId); Assert.Equal(catalogId, reference.CatalogId);
            Assert.Equal(1, reference.CatalogRevision); Assert.Equal(offered.Text, reference.Text);
            Assert.Equal(offered.Detail, reference.Detail); Assert.Equal(offered.Value, reference.Value);
            Assert.Equal("Decimal", reference.ValueType); Assert.NotNull(reference.TranslationsJson);
        }
        using var edited = await actor.PutAsJsonAsync("/api/forms/catalogs/" + catalogId, new
        {
            expectedRevision = 1, name = new { es = "Editado " + suffix, en = "Changed " + suffix }, description = new { es = "Editado", en = "Changed" },
            items = Enumerable.Range(0, 3).Select(index => CatalogItem(index, itemIds[index], index != 2, true)).ToArray(),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(first, await CaptureMultipleAsync(request));
        var inactiveRequest = request with { SourceReceiptId = Guid.NewGuid() };
        var inactive = await CaptureMultipleAsync(inactiveRequest);
        Assert.Equal("Rejected", inactive.State); Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, inactive.ErrorCode);
        Assert.Equal(catalogField.FieldId, inactive.RejectedFieldId); Assert.Null(inactive.Number);
        Assert.Equal(inactive, await CaptureMultipleAsync(inactiveRequest));
        var read = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IStructuredRecordSource>()
            .ReadAsync(actorId, culture, [request.SourceReceiptId], Ct));
        Assert.True(read.CanReadRecords);
        var reply = Assert.Single(read.Replies.Values);
        var catalogChoices = Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordChoiceValue>>(reply.Values.Single(value => value.FieldId == catalogField.FieldId).Value);
        Assert.Equal(selected.Select(id => id.ToString("D")), catalogChoices.Select(choice => choice.ChoiceId));
        Assert.Equal(culture == "en" ? new[] { "Third", "First" } : new[] { "Tercera", "Primera" }, catalogChoices.Select(choice => choice.Label));
        Assert.Equal(new[] { canonical[2], canonical[0] }, catalogChoices.Select(choice => choice.Value));
        Assert.All(catalogChoices, choice => Assert.Equal("Decimal", choice.ValueType));
        var ownChoices = Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordChoiceValue>>(reply.Values.Single(value => value.FieldId == ownField.FieldId).Value);
        Assert.Equal(ownSelected.Select(id => id.ToString("D")), ownChoices.Select(choice => choice.ChoiceId));
        Assert.All(ownChoices, choice => Assert.Null(choice.CatalogId));
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == seeded.Id).Select(value => value.LastRecordNumber).SingleAsync(Ct)));
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().CountAsync(value => value.DefinitionId == seeded.Id, Ct)));
        var run = Assert.Single(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>()
            .Where(value => value.DefinitionId == seeded.Id && value.Event == "Received").ToArrayAsync(Ct)));
        Assert.Equal(first.RecordId, run.RecordId); Assert.Equal(ruleId, run.RuleId); Assert.Equal(actionId, run.ActionId);
        Assert.Equal("NotifyAuthor", run.Kind); Assert.Equal(FormActionRunState.Waiting, run.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_multiple_arrays_persist_only_their_rejected_receipts_without_allocating_a_number(bool catalog)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        Guid? catalogId = null;
        if (catalog)
        {
            var source = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Opciones", ["en"] = "Options" }, new Dictionary<string, string>());
            var items = Enumerable.Range(0, 3).Select(index => BusinessFormCatalogItem.Create(source.Id, "C" + index, "Opción " + index, "Option " + index, null, null, index)).ToArray();
            ids = items.Select(item => item.Id).ToArray(); catalogId = source.Id;
            await factory.ExecuteDbContextAsync(async db => { db.Set<BusinessFormCatalog>().Add(source); db.Set<BusinessFormCatalogItem>().AddRange(items); await db.SaveChangesAsync(Ct); });
        }
        var field = MultipleCaptureField(true, catalog ? null : ids) with { Source = catalogId is { } id ? new(id) : null, Selection = new(2, 2) };
        var seeded = await SeedSheetAsync(actor, 0, field);
        var version = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == seeded.Id).Select(value => value.ActiveVersionId!.Value).SingleAsync(Ct));
        var prepared = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IStructuredFormSource>().PrepareAsync(actorId, "es", seeded.Id, version, false, Ct));
        Assert.True(prepared.CanSend);
        var invalid = new string?[]
        {
            JsonSerializer.Serialize(new[] { ids[0] }), JsonSerializer.Serialize(ids), JsonSerializer.Serialize(new[] { ids[0], ids[0] }),
            JsonSerializer.Serialize(new[] { ids[0], Guid.NewGuid() }), JsonSerializer.Serialize(new object[] { ids[0], 42 }), ids[0].ToString("D"), "[]", null,
        };
        var receipts = new List<Guid>();
        foreach (var raw in invalid)
        {
            var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), seeded.Id, version, actorId, factory.Clock.GetUtcNow().UtcDateTime,
                raw is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [field.Key] = raw }, prepared.Catalogs);
            receipts.Add(request.SourceReceiptId);
            var rejected = await CaptureMultipleAsync(request);
            Assert.Equal("Rejected", rejected.State); Assert.Equal(FormRecordErrors.InvalidResponse.Code, rejected.ErrorCode);
            Assert.Null(rejected.Number); Assert.Null(rejected.RecordId); Assert.Equal(rejected, await CaptureMultipleAsync(request));
        }
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == seeded.Id).Select(value => value.LastRecordNumber).SingleAsync(Ct)));
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().CountAsync(value => value.DefinitionId == seeded.Id, Ct)));
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordValue>().CountAsync(value => value.DefinitionId == seeded.Id, Ct)));
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>().CountAsync(value => value.DefinitionId == seeded.Id, Ct)));
        Assert.Equal(receipts.Count, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCaptureReceipt>()
            .CountAsync(value => receipts.Contains(value.SourceReceiptId) && value.State == "Rejected", Ct)));
    }

    [Fact]
    public async Task Optional_empty_and_omitted_multiple_selections_capture_distinct_json_without_typed_rows_or_references()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var field = MultipleCaptureField(false, [Guid.NewGuid(), Guid.NewGuid()]);
        var seeded = await SeedSheetAsync(actor, 0, field);
        var version = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == seeded.Id).Select(value => value.ActiveVersionId!.Value).SingleAsync(Ct));
        var emptyRequest = new StructuredRecordCaptureRequest(Guid.NewGuid(), seeded.Id, version, actorId,
            factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string> { [field.Key] = "[]" });
        var empty = await CaptureMultipleAsync(emptyRequest);
        var omitted = await CaptureMultipleAsync(emptyRequest with { SourceReceiptId = Guid.NewGuid(), Answers = new Dictionary<string, string>() });
        Assert.Equal("Completed", empty.State); Assert.Equal("Completed", omitted.State);
        Assert.Equal(1, empty.Number); Assert.Equal(2, omitted.Number); Assert.Equal(empty, await CaptureMultipleAsync(emptyRequest));
        var changedReplay = await CaptureMultipleAsync(emptyRequest with { Answers = new Dictionary<string, string>() });
        Assert.Equal(FormRecordErrors.ReplayConflict.Code, changedReplay.ErrorCode);
        var records = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().Where(value => value.DefinitionId == seeded.Id).OrderBy(value => value.Number).ToArrayAsync(Ct));
        Assert.Equal(2, records.Length);
        using (var json = JsonDocument.Parse(records[0].ValuesJson))
        {
            var value = Assert.Single(json.RootElement.EnumerateArray());
            Assert.Equal(field.FieldId, value.GetProperty("fieldId").GetGuid());
            Assert.Equal(JsonValueKind.Array, value.GetProperty("value").ValueKind); Assert.Equal(0, value.GetProperty("value").GetArrayLength());
        }
        Assert.Equal("[]", records[1].ValuesJson);
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordValue>().CountAsync(value => value.DefinitionId == seeded.Id, Ct)));
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>().CountAsync(value => value.DefinitionId == seeded.Id, Ct)));
    }

    [Fact]
    public async Task Real_multiple_filters_capture_keeps_each_offered_person_after_the_live_names_change()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        using var first = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        using var second = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var firstId = Guid.Parse(first.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var secondId = Guid.Parse(second.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            await users.SetDisplayNameAsync(firstId, "North " + suffix, Ct);
            await users.SetDisplayNameAsync(secondId, "South " + suffix, Ct);
            return true;
        });
        var filters = new FormCatalogFilterDefinition("name", "active", [new("name", "contains", suffix)], true, "name", false);
        var catalog = BusinessFormCatalog.CreateFilters("es", new Dictionary<string, string> { ["es"] = "Personas", ["en"] = "People" },
            new Dictionary<string, string>(), JsonSerializer.Serialize(filters, SerializerOptions));
        await factory.ExecuteDbContextAsync(async db => { db.Set<BusinessFormCatalog>().Add(catalog); await db.SaveChangesAsync(Ct); });
        var field = MultipleCaptureField(true, null) with { Source = new(catalog.Id), Selection = new(2, 2) };
        var seeded = await SeedSheetAsync(actor, 0, field);
        var version = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == seeded.Id).Select(value => value.ActiveVersionId!.Value).SingleAsync(Ct));
        var preparation = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IStructuredFormSource>()
            .PrepareAsync(actorId, "es", seeded.Id, version, false, Ct));
        Assert.True(preparation.CanSend);
        var offer = Assert.Single(preparation.Catalogs);
        Assert.Equal(new[] { firstId, secondId }, offer.Options.Select(option => option.Id));
        await factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            await users.SetDisplayNameAsync(firstId, "Changed north", Ct);
            await users.SetDisplayNameAsync(secondId, "Changed south", Ct);
            return true;
        });
        var selected = new[] { secondId, firstId };
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), seeded.Id, version, actorId, factory.Clock.GetUtcNow().UtcDateTime,
            new Dictionary<string, string> { [field.Key] = JsonSerializer.Serialize(selected) }, [offer]);
        var captured = await CaptureMultipleAsync(request);
        Assert.Equal("Completed", captured.State); Assert.Equal(1, captured.Number);
        Assert.Equal(captured, await CaptureMultipleAsync(request));
        var references = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>().Where(value => value.RecordId == captured.RecordId).ToArrayAsync(Ct));
        Assert.Equal(2, references.Length);
        foreach (var id in selected)
        {
            var expected = Assert.Single(offer.Options, option => option.Id == id);
            var reference = Assert.Single(references, value => value.ItemId == id);
            Assert.Equal(expected.Text, reference.Text); Assert.Equal("Activo", reference.Detail);
            Assert.Equal(catalog.Id, reference.CatalogId); Assert.Null(reference.Value); Assert.Null(reference.ValueType);
        }
        var values = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordValue>().Where(value => value.RecordId == captured.RecordId).OrderBy(value => value.Position).ToArrayAsync(Ct));
        Assert.Equal(new[] { 0, 1 }, values.Select(value => value.Position));
        Assert.Equal(selected, values.Select(value => value.ChoiceId!.Value));
        var read = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IStructuredRecordSource>().ReadAsync(actorId, "es", [request.SourceReceiptId], Ct));
        var answer = Assert.Single(read.Replies.Values).Values.Single(value => value.FieldId == field.FieldId);
        var choices = Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordChoiceValue>>(answer.Value);
        Assert.Equal(new[] { "South " + suffix, "North " + suffix }, choices.Select(choice => choice.Label));
    }
    private async Task<StructuredRecordCaptureResponse> CaptureMultipleAsync(StructuredRecordCaptureRequest request)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().CaptureAsync(request, Ct);
    }

    private static CanonicalFormField MultipleCaptureField(bool required, IReadOnlyList<Guid>? ids)
    {
        var fieldId = Guid.NewGuid();
        return new(fieldId, "field_" + fieldId.ToString("N"), "multiChoice",
            new Dictionary<string, string> { ["es"] = "Niveles", ["en"] = "Levels" }, null, required, null,
            ids?.Select((id, index) => new CanonicalFormChoice(id, new Dictionary<string, string> { ["es"] = "Opción " + index, ["en"] = "Option " + index })).ToArray(),
            Selection: new(required ? 1 : 0, 2));
    }
}
