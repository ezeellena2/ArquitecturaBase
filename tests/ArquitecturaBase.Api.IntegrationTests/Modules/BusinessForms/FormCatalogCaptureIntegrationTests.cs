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
    [Fact]
    public async Task Catalog_capture_preserves_the_offer_after_rename_and_rejects_deactivation_without_numbering()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        using var created = await actor.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Personas", en = "People" }, description = new { es = "Lista", en = "List" },
            items = new[] { new { localId = "item", code = "PERSON", text = new { es = "Texto original" },
                detail = new { es = "Detalle original" }, isActive = true } },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogJson = await created.ReadJsonAsync();
        var catalogId = catalogJson.GetProperty("catalog").GetProperty("id").GetGuid();
        var itemId = catalogJson.GetProperty("itemIdentityMap").GetProperty("item").GetGuid();
        var form = await SeedCatalogVersionAsync(actorId, catalogId);
        using var scope = factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
        var preparation = await source.PrepareAsync(actorId, "en", form.DefinitionId, form.VersionId, false, Ct);
        Assert.True(preparation.CanSend);
        var offer = Assert.Single(preparation.Catalogs);
        Assert.Equal("Texto original", Assert.Single(offer.Options).Text);
        Assert.Equal("Detalle original", offer.Options[0].Detail);
        Assert.Equal("People", offer.CatalogName);
        using var changed = await actor.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", CatalogEdit(1, true, "Texto nuevo"), Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), form.DefinitionId, form.VersionId, actorId,
            factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string> { [offer.Key] = itemId.ToString("D") }, [offer]);
        var sink = scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>();
        var captured = await sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", captured.State);
        Assert.Equal(captured, await sink.CaptureAsync(request, Ct));
        var reference = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>()
            .SingleAsync(value => value.RecordId == captured.RecordId, Ct));
        Assert.Equal(catalogId, reference.CatalogId);
        Assert.Equal(1, reference.CatalogRevision);
        Assert.Equal(itemId, reference.ItemId);
        Assert.Equal("Texto original", reference.Text);
        Assert.Equal("Detalle original", reference.Detail);
        using var inactive = await actor.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", CatalogEdit(2, false, "Texto nuevo"), Ct);
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        using var captureScope = factory.Services.CreateScope();
        var anotherSink = captureScope.ServiceProvider.GetRequiredService<IStructuredRecordSink>();
        var secondRequest = request with { SourceReceiptId = Guid.NewGuid() };
        var rejected = await anotherSink.CaptureAsync(secondRequest, Ct);
        Assert.Equal("Rejected", rejected.State);
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, rejected.ErrorCode);
        Assert.Equal(offer.FieldId, rejected.RejectedFieldId);
        Assert.Equal(rejected, await anotherSink.CaptureAsync(secondRequest, Ct));
        Assert.Null(rejected.Number);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>()
            .Where(value => value.Id == form.DefinitionId).Select(value => value.LastRecordNumber).SingleAsync(Ct)));
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>()
            .CountAsync(value => value.DefinitionId == form.DefinitionId, Ct)));
        using var reloadScope = factory.Services.CreateScope();
        var reader = reloadScope.ServiceProvider.GetRequiredService<IStructuredRecordSource>();
        var read = await reader.ReadAsync(actorId, "es", [request.SourceReceiptId], Ct);
        var answer = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(Assert.Single(read.Replies).Value.Values).Value);
        Assert.Equal("Texto original", answer.Label);
        Assert.Equal("Personas", answer.CatalogName);
        Assert.Equal("Detalle original", answer.Detail);

        using var facets = await actor.PostAsJsonAsync($"/api/forms/definitions/{form.DefinitionId}/records/filter-counts/search",
            new { dimension = "choice", fieldId = offer.FieldId, search = "original", pageSize = 1, filter = new { } }, Ct);
        Assert.Equal(HttpStatusCode.OK, facets.StatusCode);
        var facet = Assert.Single((await facets.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(itemId.ToString(), facet.GetProperty("id").GetString());
        Assert.Equal("Texto original", facet.GetProperty("label").GetString());
        Assert.Equal(1, facet.GetProperty("count").GetInt32());

        object CatalogEdit(long revision, bool active, string text) => new
        {
            expectedRevision = revision, name = new { es = "Personas", en = "People" }, description = new { es = "Lista", en = "List" },
            items = new[] { new { localId = "item", itemId, code = "PERSON", text = new { es = text }, detail = new { es = "Detalle nuevo" }, isActive = active } },
        };
    }

    [Fact]
    public async Task Captured_manual_translations_are_read_from_the_offer_after_the_catalog_changes()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var catalog = BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Sucursales", ["en"] = "Branches" },
            new Dictionary<string, string> { ["es"] = "Sucursales disponibles", ["en"] = "Available branches" });
        var item = BusinessFormCatalogItem.Create(catalog.Id, "NORTH", "Sucursal norte", "North branch", "Planta baja", "Ground floor", 0);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormCatalog>().Add(catalog);
            db.Set<BusinessFormCatalogItem>().Add(item);
            await db.SaveChangesAsync(Ct);
        });
        var form = await SeedCatalogVersionAsync(actorId, catalog.Id);
        using var scope = factory.Services.CreateScope();
        var offer = Assert.Single((await scope.ServiceProvider.GetRequiredService<IStructuredFormSource>()
            .PrepareAsync(actorId, "en", form.DefinitionId, form.VersionId, false, Ct)).Catalogs);
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), form.DefinitionId, form.VersionId, actorId,
            factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string> { [offer.Key] = item.Id.ToString("D") }, [offer]);
        var captured = await scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().CaptureAsync(request, Ct);
        Assert.Equal("Completed", captured.State);
        using var changed = await actor.PutAsJsonAsync($"/api/forms/catalogs/{catalog.Id}", new
        {
            expectedRevision = 1, name = new { es = "Sucursales nuevas", en = "Changed branches" }, description = new { es = "Sucursales renombradas", en = "Renamed branches" },
            items = new[] { new { localId = "existing", itemId = item.Id, code = "NORTH",
                text = new { es = "Texto nuevo", en = "Changed label" }, isActive = true } }
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var readScope = factory.Services.CreateScope();
        var reader = readScope.ServiceProvider.GetRequiredService<IStructuredRecordSource>();
        foreach (var (culture, expected, detail, name) in new[]
        {
            ("es", "Sucursal norte", "Planta baja", "Sucursales"),
            ("en", "North branch", "Ground floor", "Branches")
        })
        {
            var read = await reader.ReadAsync(actorId, culture, [request.SourceReceiptId], Ct);
            var answer = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(Assert.Single(read.Replies).Value.Values).Value);
            Assert.Equal(expected, answer.Label);
            Assert.Equal(detail, answer.Detail);
            Assert.Equal(name, answer.CatalogName);
        }
        var reference = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>()
            .SingleAsync(value => value.RecordId == captured.RecordId, Ct));
        Assert.NotNull(reference.TranslationsJson);
        Assert.Contains("North branch", reference.TranslationsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filters_are_prepared_per_recipient_and_capture_keeps_the_offer_after_source_changes()
    {
        using var first = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        using var second = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var firstId = Guid.Parse(first.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var secondId = Guid.Parse(second.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            await users.SetDisplayNameAsync(firstId, "First " + suffix, Ct);
            await users.SetDisplayNameAsync(secondId, "Second " + suffix, Ct);
            return true;
        });
        var filters = new FormCatalogFilterDefinition("name", "active", [new("name", "contains", suffix)], true, "name", false);
        var catalog = BusinessFormCatalog.CreateFilters(new Dictionary<string, string> { ["es"] = "Personas", ["en"] = "People" },
            new Dictionary<string, string>(), JsonSerializer.Serialize(filters, SerializerOptions));
        await factory.ExecuteDbContextAsync(async db => { db.Set<BusinessFormCatalog>().Add(catalog); await db.SaveChangesAsync(Ct); });
        var form = await SeedCatalogVersionAsync(firstId, catalog.Id);
        using var scope = factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IStructuredFormSource>();
        var forFirst = await source.PrepareAsync(firstId, "es", form.DefinitionId, form.VersionId, false, Ct);
        var forSecond = await source.PrepareAsync(secondId, "en", form.DefinitionId, form.VersionId, false, Ct);
        Assert.True(forFirst.CanSend); Assert.True(forSecond.CanSend);
        Assert.Equal(secondId, Assert.Single(Assert.Single(forFirst.Catalogs).Options).Id);
        Assert.Equal(firstId, Assert.Single(Assert.Single(forSecond.Catalogs).Options).Id);
        Assert.Equal("Activo", Assert.Single(Assert.Single(forFirst.Catalogs).Options).Detail);
        Assert.Equal("Active", Assert.Single(Assert.Single(forSecond.Catalogs).Options).Detail);
        var offer = Assert.Single(forFirst.Catalogs);
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().SetDisplayNameAsync(secondId, "Renamed", Ct);
            return true;
        });
        using var captureScope = factory.Services.CreateScope();
        var sink = captureScope.ServiceProvider.GetRequiredService<IStructuredRecordSink>();
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), form.DefinitionId, form.VersionId, firstId,
            factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string> { [offer.Key] = secondId.ToString("D") }, [offer]);
        var result = await sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", result.State);
        var reference = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>()
            .SingleAsync(value => value.RecordId == result.RecordId, Ct));
        Assert.Equal(secondId, reference.ItemId);
        Assert.Equal("Second " + suffix, reference.Text);
        using var refreshedScope = factory.Services.CreateScope();
        var refreshed = await refreshedScope.ServiceProvider.GetRequiredService<IStructuredFormSource>()
            .PrepareAsync(firstId, "es", form.DefinitionId, form.VersionId, false, Ct);
        Assert.False(refreshed.CanSend);
        Assert.Equal(FormRecordErrors.CatalogEmpty.Code, Assert.Single(refreshed.Issues).Code);
    }

    [Fact]
    public async Task A_reference_insert_failure_rolls_back_the_response_receipt_values_and_number()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsCreate]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var catalog = BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Personas", ["en"] = "People" }, new Dictionary<string, string>());
        var item = BusinessFormCatalogItem.Create(catalog.Id, "PERSON", "Original", null, null, null, 0);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormCatalog>().Add(catalog); db.Set<BusinessFormCatalogItem>().Add(item); await db.SaveChangesAsync(Ct);
        });
        var form = await SeedCatalogVersionAsync(actorId, catalog.Id);
        using var preparationScope = factory.Services.CreateScope();
        var preparation = await preparationScope.ServiceProvider.GetRequiredService<IStructuredFormSource>()
            .PrepareAsync(actorId, "es", form.DefinitionId, form.VersionId, false, Ct);
        var offer = Assert.Single(preparation.Catalogs);
        var request = new StructuredRecordCaptureRequest(Guid.NewGuid(), form.DefinitionId, form.VersionId, actorId,
            factory.Clock.GetUtcNow().UtcDateTime, new Dictionary<string, string> { [offer.Key] = item.Id.ToString("D") }, [offer]);
        var trigger = "K7Reference" + Guid.NewGuid().ToString("N");
        var create = $"""
            CREATE FUNCTION "{trigger}"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."CatalogId" = '{catalog.Id:D}'::uuid THEN
                    RAISE EXCEPTION 'Reference fixture failure' USING ERRCODE = 'P0001';
                END IF;
                RETURN NEW;
            END; $$;
            CREATE TRIGGER "{trigger}" BEFORE INSERT ON "BusinessFormRecordReferences"
            FOR EACH ROW EXECUTE FUNCTION "{trigger}"();
            """;
        await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlRawAsync(create, Ct));
        try
        {
            using var captureScope = factory.Services.CreateScope();
            await Assert.ThrowsAsync<DbUpdateException>(() => captureScope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().CaptureAsync(request, Ct));
            Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().CountAsync(value => value.DefinitionId == form.DefinitionId, Ct)));
            Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordValue>().CountAsync(value => value.DefinitionId == form.DefinitionId, Ct)));
            Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>().CountAsync(value => value.DefinitionId == form.DefinitionId, Ct)));
            Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCaptureReceipt>().CountAsync(value => value.SourceReceiptId == request.SourceReceiptId, Ct)));
            Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == form.DefinitionId).Select(value => value.LastRecordNumber).SingleAsync(Ct)));
        }
        finally
        {
            var drop = $"""DROP TRIGGER "{trigger}" ON "BusinessFormRecordReferences"; DROP FUNCTION "{trigger}"();""";
            await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlRawAsync(drop, Ct));
        }
        using var retryScope = factory.Services.CreateScope();
        var captured = await retryScope.ServiceProvider.GetRequiredService<IStructuredRecordSink>().CaptureAsync(request, Ct);
        Assert.Equal("Completed", captured.State);
        Assert.Equal(1, captured.Number);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordReference>().CountAsync(value => value.RecordId == captured.RecordId, Ct)));
    }

    [Fact]
    public async Task Catalog_facets_and_record_order_use_historical_ui_text_with_spanish_and_legacy_fallbacks()
    {
        using var reader=await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var actor=Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var catalog=BusinessFormCatalog.CreateManual(new Dictionary<string,string>{{"es","Actual"},{"en","Current"}},new Dictionary<string,string>());
        var items=Enumerable.Range(0,4).Select(index=>BusinessFormCatalogItem.Create(catalog.Id,"ITEM"+index,"Texto actual "+index,"Current label "+index,null,null,index)).ToArray();
        await factory.ExecuteDbContextAsync(async db=>{db.Set<BusinessFormCatalog>().Add(catalog);db.Set<BusinessFormCatalogItem>().AddRange(items);await db.SaveChangesAsync(Ct);});
        var form=await SeedCatalogVersionAsync(actor,catalog.Id);var ids=new List<Guid>();Guid fieldId=default;
        await factory.ExecuteDbContextAsync(async db=>
        {
            var definition=await db.Set<BusinessFormDefinition>().SingleAsync(value=>value.Id==form.DefinitionId,Ct);
            var version=await db.Set<BusinessFormVersion>().SingleAsync(value=>value.Id==form.VersionId,Ct);
            fieldId=JsonSerializer.Deserialize<FormVersionSchema>(version.SchemaJson,SerializerOptions)!.Questions.Single().FieldId;
            var samples=new[]{(Item:0,Offered:"Zulu",Es:(string?)"Alfa",En:(string?)"Zulu"),(Item:1,Offered:"Zeta",Es:(string?)"Zeta",En:(string?)"Alpha"),
                (Item:2,Offered:"Medio",Es:(string?)"Medio",En:(string?)null),(Item:3,Offered:"Lima",Es:(string?)null,En:(string?)null),
                (Item:0,Offered:"Zulu recent",Es:(string?)"Alfa reciente",En:(string?)"Zulu recent")};
            foreach(var sample in samples)
            {
                var item=items[sample.Item];var receipt=Guid.NewGuid();var now=factory.Clock.GetUtcNow().UtcDateTime;
                var values=JsonSerializer.Serialize(new[]{new{fieldId,value=item.Id}},SerializerOptions);
                var record=BusinessFormRecord.Create(definition.Id,version.Id,definition.AllocateRecordNumber(),actor,receipt,now,values);ids.Add(record.Id);
                Dictionary<string,FormRecordReferenceTextResponse>? texts=sample.Es is null?null:new(){{"es",new(sample.Es,null,"Original catalog")}};
                if(sample.En is {} en)texts!["en"]=new(en,null,"Original catalog");
                db.Set<BusinessFormRecord>().Add(record);db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id,definition.Id,fieldId,"choice",choiceId:item.Id));
                var reference=BusinessFormRecordReference.Create(record.Id,definition.Id,fieldId,catalog.Id,1,item.Id,sample.Offered,null,"Offered catalog",texts is null?null:JsonSerializer.Serialize(texts,SerializerOptions));
                db.Entry(reference).Property(value=>value.Id).CurrentValue=Guid.CreateVersion7(new DateTimeOffset(now).AddMinutes(ids.Count));
                db.Set<BusinessFormRecordReference>().Add(reference);
                db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receipt,definition.Id,version.Id,"historical-labels",record.Id,record.Number,now));
            }
            await db.SaveChangesAsync(Ct);
        });
        foreach(var culture in new[]{"es","en"})
        {
            reader.DefaultRequestHeaders.AcceptLanguage.Clear();reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var ordered=await reader.PostAsJsonAsync($"/api/forms/definitions/{form.DefinitionId}/records/search",new{sort="field",sortFieldId=fieldId,pageSize=20},Ct);
            Assert.Equal(HttpStatusCode.OK,ordered.StatusCode);
            var recordIds=(await ordered.ReadJsonAsync()).GetProperty("records").GetProperty("items").EnumerateArray().Select(row=>row.GetProperty("id").GetGuid()).ToArray();
            Assert.Equal(culture=="es"?new[]{ids[0],ids[4],ids[3],ids[2],ids[1]}:new[]{ids[1],ids[3],ids[2],ids[0],ids[4]},recordIds);
            using var facets=await reader.PostAsJsonAsync($"/api/forms/definitions/{form.DefinitionId}/records/filter-counts/search",new{dimension="choice",fieldId,sort="label",pageSize=20,filter=new{}},Ct);
            Assert.Equal(HttpStatusCode.OK,facets.StatusCode);var options=(await facets.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(culture=="es"?new[]{"Alfa reciente","Lima","Medio","Zeta"}:new[]{"Alpha","Lima","Medio","Zulu recent"},options.Select(option=>option.GetProperty("label").GetString()));
            Assert.Equal(2,options.Single(option=>option.GetProperty("id").GetGuid()==items[0].Id).GetProperty("count").GetInt32());
            using var searched=await reader.PostAsJsonAsync($"/api/forms/definitions/{form.DefinitionId}/records/filter-counts/search",new{dimension="choice",fieldId,search=culture=="es"?"Alfa":"Alpha",pageSize=20,filter=new{}},Ct);
            Assert.Equal(HttpStatusCode.OK,searched.StatusCode);var match=Assert.Single((await searched.ReadJsonAsync()).GetProperty("items").EnumerateArray());
            Assert.Equal(items[culture=="es"?0:1].Id,match.GetProperty("id").GetGuid());Assert.Equal(culture=="es"?2:1,match.GetProperty("count").GetInt32());
        }
    }

    private async Task<(Guid DefinitionId, Guid VersionId)> SeedCatalogVersionAsync(Guid actor, Guid catalogId)
    {
        var field = Field("choice", true) with { Source = new(catalogId) };
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Catálogo", "{\"es\":\"Catálogo\",\"en\":\"Catalog\"}", "K" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), "es",
            "{\"es\":\"Enviar\",\"en\":\"Submit\"}", JsonSerializer.Serialize(new FormQuestionSchema(1, [field]), SerializerOptions), 1, true).IsSuccess);
        var version = BusinessFormVersion.Create(definition.Id, 1, new string('a', 64));
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, [field]), SerializerOptions));
        version.SealPresentation("{\"es\":\"Catálogo\",\"en\":\"Catalog\"}", "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", 1);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var operation = BusinessFormPublicationOperation.Accept(definition.Id, version.Id, actor, Guid.NewGuid(), 1, 1, null, [1], now);
        var fence = operation.Reserve(now, TimeSpan.FromMinutes(1)).Value;
        Assert.True(operation.RecordChannelResult(fence, now, "Published", "Verify").IsSuccess);
        Assert.True(operation.Complete(fence, now).IsSuccess);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(operation);
            await db.SaveChangesAsync(Ct);
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        return (definition.Id, version.Id);
    }
}
