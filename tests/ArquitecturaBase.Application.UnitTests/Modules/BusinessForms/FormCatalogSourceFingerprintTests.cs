using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogSourceFingerprintTests
{
    [Fact]
    public void An_old_catalog_source_and_offer_keep_their_exact_json_when_new_properties_are_null()
    {
        var id = Guid.Parse("01990000-0000-7000-8000-000000000001");
        Assert.Equal("{\"catalog\":\"01990000-0000-7000-8000-000000000001\"}", FormDraftPolicy.Serialize(new FormQuestionSource(id)));
        var option = new StructuredFormCatalogOption(id, "Original", "Detail");
        Assert.Equal("{\"id\":\"01990000-0000-7000-8000-000000000001\",\"text\":\"Original\",\"detail\":\"Detail\"}", FormDraftPolicy.Serialize(option));
        Assert.Null(JsonSerializer.Deserialize<StructuredFormCatalogOption>(FormDraftPolicy.Serialize(option), JsonSerializerOptions.Web)!.Shown);
    }

    [Fact]
    public void A_pre_E2_version_keeps_its_original_complete_schema_and_content_hash()
    {
        const string schema = """{"format":1,"questions":[{"fieldId":"01990000-0000-7000-8000-000000000001","key":"field_01990000000070008000000000000001","type":"choice","label":{"es":"Proveedor"},"help":null,"required":true,"maxLength":null,"options":null,"source":{"catalog":"01990000-0000-7000-8000-000000000002"}}]}""";
        var restored = FormDraftPolicy.Read<FormVersionSchema>(schema);
        var canonical = FormDraftPolicy.Serialize(restored);
        Assert.Equal(schema, canonical);
        Assert.Equal("5eed2fefc8fc3d9395e3431ba0e552f907e33dcd97f013fe1c92c8c748c38bc4", FormPublicationSnapshotPolicy.Hash(canonical));
    }

    [Fact]
    public void Normalization_orders_matches_and_turns_empty_lists_into_null()
    {
        var first = Guid.Parse("01990000-0000-7000-8000-000000000001");
        var last = Guid.Parse("01990000-0000-7000-8000-000000000002");
        var request = Input(new(Guid.CreateVersion7(), [new(last, "z"), new(first, "a")]));
        var normalized = FormDraftPolicy.Normalize(request);
        Assert.Equal([first, last], normalized.Draft.Fields[0].Source!.Match!.Select(match => match.FieldId));
        var empty = FormDraftPolicy.Normalize(Input(new(request.Draft.Fields[0].Source!.Catalog, [])));
        Assert.Null(empty.Draft.Fields[0].Source!.Match);
        Assert.DoesNotContain("match", FormDraftPolicy.Serialize(empty.Draft.Fields[0].Source));
    }

    [Fact]
    public void Editing_match_and_presentation_keeps_the_catalog_question_identity()
    {
        var catalog = Guid.CreateVersion7();
        var created = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(Input(new(catalog))), null, FormRetiredIdentities.Empty).Value;
        var field = created.Draft.Fields[0];
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Formulario", "{\"es\":\"Formulario\"}", "CAT", "es", "{\"es\":\"Enviar\"}",
            FormDraftPolicy.Serialize(new FormQuestionSchema(1, created.Draft.Fields)), 1, true).IsSuccess);
        var edited = Input(new(catalog, [new(Guid.CreateVersion7(), "Frío")], new(["code"], ["detail"])));
        edited = edited with { Draft = edited.Draft with { Fields = [edited.Draft.Fields[0] with { FieldId = field.FieldId }] } };
        var result = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(edited), definition, FormRetiredIdentities.Empty);
        Assert.True(result.IsSuccess);
        Assert.Equal(field.FieldId, Assert.Single(result.Value.Draft.Fields).FieldId);
        Assert.Empty(result.Value.NewlyRetiredIdentities.Fields);
        Assert.Equal(edited.Draft.Fields[0].Source!.Presentation, result.Value.Draft.Fields[0].Source!.Presentation);
    }

    [Fact]
    public void The_shown_presentation_is_sealed_separately_from_the_original_choice_text()
    {
        var option = new StructuredFormCatalogOption(Guid.CreateVersion7(), "Original", "Original detail", Shown: new("Visible", "Visible detail"));
        var roundTrip = JsonSerializer.Deserialize<StructuredFormCatalogOption>(FormDraftPolicy.Serialize(option), JsonSerializerOptions.Web)!;
        Assert.Equal("Original", roundTrip.Text);
        Assert.Equal("Original detail", roundTrip.Detail);
        Assert.Equal(new StructuredFormCatalogShown("Visible", "Visible detail"), roundTrip.Shown);
    }

    private static SaveFormDraftRequest Input(FormQuestionSource source) => new(Guid.CreateVersion7(),
        new Dictionary<string, string> { ["es"] = "Formulario" }, "CAT", new(1, "es", new Dictionary<string, string> { ["es"] = "Enviar" },
            [new("question", null, null, "choice", new Dictionary<string, string> { ["es"] = "Opciones" }, null, true, null, null, source)]));
}
