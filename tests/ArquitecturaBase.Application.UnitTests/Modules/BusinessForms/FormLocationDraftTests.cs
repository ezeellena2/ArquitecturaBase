using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// UB1-A: «Ubicación» is a question of its own, only required or optional: no options, files, selection, length, calculation
// or condition of its own, and never inside the block of rows. Since W1-A it publishes also with WhatsApp, which asks for it
// before the Flow; the channel's compiler decides what it takes.
public sealed class FormLocationDraftTests
{
    private static readonly IReadOnlyDictionary<string, string> Text = new Dictionary<string, string> { ["es"] = "Dónde estás", ["en"] = "Where you are" };

    private static FormFieldInput Location(bool required = true) => new("place", null, null, "location", Text, null, required, null, null);

    private static SaveFormDraftRequest Request(params FormFieldInput[] fields) =>
        new(Guid.NewGuid(), Text, "VIS", new(1, "es", Text, fields, AppOnly: true));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_required_or_optional_location_is_saved_as_its_own_question(bool required)
    {
        var request = Request(Location(required));

        Assert.True(new SaveFormDraftRequestValidator().Validate(request).IsValid);
        var resolved = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request), null, FormRetiredIdentities.Empty);

        Assert.True(resolved.IsSuccess);
        var field = Assert.Single(resolved.Value.Draft.Fields);
        Assert.Equal("location", field.Type);
        Assert.Equal(required, field.Required);
        Assert.Null(field.Options);
        Assert.Null(field.Files);
        Assert.Null(field.Selection);
        Assert.Null(field.MaxLength);
        Assert.Equal(1, FormDraftPolicy.QuestionCount(resolved.Value.Draft.Fields));
    }

    public static TheoryData<FormFieldInput, string> Settings => new()
    {
        { Location() with { Options = [new("option", null, new Dictionary<string, string> { ["es"] = "Sí" })] }, "draft.fields[0].options" },
        { Location() with { Files = new(1, 1) }, "draft.fields[0].files" },
        { Location() with { Selection = new(1, 1) }, "draft.fields[0].selection" },
        { Location() with { MaxLength = 10 }, "draft.fields[0].maxLength" },
        { Location() with { Source = new(Guid.NewGuid()) }, "draft.fields[0].source.catalog" },
        { Location(false) with { Calculation = new(FormCalculation.Sum, [Guid.NewGuid()]) }, "draft.fields[0].calculation" },
        { Location() with { ShowWhen = new(Guid.NewGuid(), ["true"]) }, "draft.fields[0].showWhen" },
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public void A_location_has_no_settings_besides_being_required(FormFieldInput field, string path)
    {
        var errors = new SaveFormDraftRequestValidator().Validate(Request(field)).Errors;

        Assert.Contains(errors, error => error.PropertyName == path);
    }

    [Fact]
    public void A_location_never_goes_inside_the_block_of_rows()
    {
        var block = new FormFieldInput("block", null, null, "group", Text, null, true, null, null, Rows: new(1, 10));
        var quantity = new FormFieldInput("quantity", null, null, "decimal", Text, null, false, null, null, Repeated: true);

        var errors = new SaveFormDraftRequestValidator().Validate(Request(block, quantity, Location() with { Repeated = true })).Errors;

        Assert.Contains(errors, error => error.PropertyName == "draft.fields[2].repeated");
    }

    [Fact]
    public void Changing_a_saved_question_to_a_location_is_another_question()
    {
        var first = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(Request(new FormFieldInput("place", null, null, "shortText", Text, null, true, 120, null))),
            null, FormRetiredIdentities.Empty).Value;
        var saved = first.Draft.Fields[0];
        var existing = BusinessFormDefinition.Create();
        Assert.True(existing.SetDraft(1, "Visita", FormDraftPolicy.Serialize(Text), "VIS", "es", FormDraftPolicy.Serialize(Text),
            FormDraftPolicy.Serialize(new FormQuestionSchema(1, first.Draft.Fields)), 0, true, appOnly: true).IsSuccess);

        var kept = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(Request(Location() with { FieldId = saved.FieldId })),
            existing, FormRetiredIdentities.Empty);
        var replaced = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(Request(Location() with { ReplacesFieldId = saved.FieldId })),
            existing, FormRetiredIdentities.Empty);

        Assert.True(kept.IsFailure);
        Assert.True(replaced.IsSuccess);
        var location = Assert.Single(replaced.Value.Draft.Fields);
        Assert.Equal("location", location.Type);
        Assert.NotEqual(saved.FieldId, location.FieldId);
        Assert.Contains(saved.FieldId, replaced.Value.NewlyRetiredIdentities.Fields);
    }

    private static CanonicalFormField Field(string type) =>
        new(Guid.CreateVersion7(), "field_x", type, Text, null, true, type == "shortText" ? 120 : null, null);

    private static FormDefinitionResponse Definition(bool? appOnly, params CanonicalFormField[] fields) =>
        new(Guid.NewGuid(), Text, "VIS", false, 1, new FormDraftResponse(1, "es", Text, fields, null, appOnly));

    [Fact]
    public void No_condition_and_no_calculation_rests_on_a_location()
    {
        var location = Field("location");
        var shown = Field("shortText") with { ShowWhen = new(location.FieldId, ["true"]) };
        var total = new CanonicalFormField(Guid.CreateVersion7(), "field_y", "decimal", Text, null, false, null, null,
            Calculation: new(FormCalculation.Sum, [location.FieldId]));
        var definition = Definition(true, location, shown, total);

        Assert.Equal(FormPublicationErrors.ConditionInvalid.Code, Assert.Single(FormPublicationPolicy.ValidateConditions(definition)).Code);
        Assert.Equal(FormPublicationErrors.CalculationInvalid.Code, Assert.Single(FormPublicationPolicy.ValidateCalculations(definition)).Code);
    }
}
