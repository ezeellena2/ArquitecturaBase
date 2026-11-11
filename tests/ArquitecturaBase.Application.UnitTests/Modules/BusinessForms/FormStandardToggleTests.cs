using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// IV2-A: a library standard declares yes-or-no toggles on a bound question («El precio incluye IVA»), with their key, text and (i)
// by language and their default value. The copy keeps them in the question: only their value changes, only a bound question has
// them, and the issuer receives each one as a datum of its own, "true" or "false".
public sealed class FormStandardToggleTests
{
    private static readonly Guid Items = Guid.CreateVersion7(), Price = Guid.CreateVersion7(), Notes = Guid.CreateVersion7();
    private static readonly string Bindings = FormDraftPolicy.Serialize(new Dictionary<string, Guid> { ["items"] = Items, ["unitPrice"] = Price });

    private static FormQuestionToggle Toggle(bool value, string key = "pricesIncludeVat", string label = "El precio incluye IVA") =>
        new(key, new Dictionary<string, string> { ["en"] = "Prices include VAT", ["es"] = label },
            new Dictionary<string, string> { ["en"] = "Check it if you enter final prices.", ["es"] = "Marcala si cargás precios finales." }, value);

    private static CanonicalFormField Field(Guid id, string type, bool? repeated = null, IReadOnlyList<FormQuestionToggle>? toggles = null) =>
        new(id, "field_" + id.ToString("N"), type, new Dictionary<string, string> { ["es"] = "Dato" }, null, type == "group", null, null,
            Rows: type == "group" ? new(1, 5) : null, Repeated: repeated, Toggles: toggles);

    private static readonly CanonicalFormField[] Saved =
        [Field(Items, "group"), Field(Price, "decimal", true, [Toggle(false)]), Field(Notes, "longText")];

    [Fact]
    public void The_save_keeps_the_toggles_of_a_question_with_their_texts_in_order()
    {
        var text = new Dictionary<string, string> { ["es"] = "Remito" };
        var request = new SaveFormDraftRequest(Guid.NewGuid(), text, "REM", new(1, "es", text,
        [
            new FormFieldInput("price", null, null, "decimal", new Dictionary<string, string> { ["es"] = "Precio unitario" }, null, true, null, null, Toggles: [Toggle(true)]),
            new FormFieldInput("notes", null, null, "longText", new Dictionary<string, string> { ["es"] = "Notas" }, null, false, null, null),
        ]));
        Assert.Empty(new SaveFormDraftRequestValidator().Validate(request).Errors);

        var fields = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request), null, FormRetiredIdentities.Empty).Value.Draft.Fields;

        var toggle = Assert.Single(fields[0].Toggles!);
        Assert.Equal(("pricesIncludeVat", true), (toggle.Key, toggle.Value));
        Assert.Equal(["en", "es"], toggle.Label.Keys);
        Assert.Contains("\"toggles\":[{\"key\":\"pricesIncludeVat\",\"label\":{\"en\":\"Prices include VAT\",\"es\":\"El precio incluye IVA\"}", FormDraftPolicy.Serialize(fields[0]));
        Assert.DoesNotContain("toggles", FormDraftPolicy.Serialize(fields[1]));
    }

    [Fact]
    public void A_toggle_has_its_key_text_and_help_and_no_key_repeats()
    {
        static string[] Errors(params FormQuestionToggle[] toggles)
        {
            var text = new Dictionary<string, string> { ["es"] = "Remito" };
            return new SaveFormDraftRequestValidator().Validate(new SaveFormDraftRequest(Guid.NewGuid(), text, "REM", new(1, "es", text,
                [new FormFieldInput("price", null, null, "decimal", text, null, true, null, null, Toggles: toggles)]))).Errors.Select(error => error.PropertyName).ToArray();
        }

        Assert.Contains("draft.fields[0].toggles", Errors());
        Assert.Contains("draft.fields[0].toggles", Errors(Toggle(true) with { Key = "" }));
        Assert.Contains("draft.fields[0].toggles", Errors(Toggle(true) with { Label = null! }));
        Assert.Contains("draft.fields[0].toggles", Errors(Toggle(true) with { Help = null! }));
        Assert.Contains("draft.fields[0].toggles", Errors(Toggle(true), Toggle(false)));
        Assert.Contains("draft.fields[0].toggles", Errors((FormQuestionToggle)null!));
    }

    [Fact]
    public void Only_the_value_of_a_toggle_changes_in_a_bound_question()
    {
        Assert.True(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[0], Saved[1] with { Toggles = [Toggle(true)] }, Saved[2]]).IsSuccess);

        Assert.Equal("draft.fields[1]", BoundPath(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[0], Saved[1] with { Toggles = [Toggle(false, label: "Con IVA")] }])));
        Assert.Equal("draft.fields[1]", BoundPath(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[0], Saved[1] with { Toggles = [Toggle(false, "pricesWithVat")] }])));
        Assert.Equal("draft.fields[1]", BoundPath(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[0], Saved[1] with { Toggles = [Toggle(false), Toggle(false, "other")] }])));
        Assert.Equal("draft.fields[1]", BoundPath(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[0], Saved[1] with { Toggles = null }])));
    }

    [Fact]
    public void Only_a_bound_question_has_toggles()
    {
        Assert.True(FormStandardPolicy.CheckDeclared([Items, Price], Saved).IsSuccess);
        Assert.True(FormStandardPolicy.CheckDeclared([], [Saved[0], Saved[2]]).IsSuccess);

        var refused = FormStandardPolicy.CheckDeclared([Items, Price], [Saved[0], Saved[1], Saved[2] with { Toggles = [Toggle(true)] }]);
        Assert.True(refused.IsFailure);
        Assert.Equal(FormDraftErrors.QuestionConfigurationInvalid.Code, refused.Error.Code);
        Assert.Equal("draft.fields[2].toggles", (refused.Error as ValidationError)?.Errors.Keys.Single());
        Assert.Equal("draft.fields[1].toggles", (FormStandardPolicy.CheckDeclared([], Saved).Error as ValidationError)?.Errors.Keys.Single());
    }

    [Fact]
    public void The_issuer_receives_each_toggle_of_a_bound_question_as_a_datum_answered_or_not()
    {
        var schema = JsonSerializer.Serialize(new FormVersionSchema(1, [Saved[0], Saved[1] with { Toggles = [Toggle(true), Toggle(false, "other")] }, Saved[2]]),
            JsonSerializerOptions.Web);
        var bound = new Dictionary<string, Guid> { ["items"] = Items, ["unitPrice"] = Price };
        var answered = JsonSerializer.Serialize(new object[] { new { fieldId = Items, value = new[] { new[] { new { fieldId = Price, value = 120.5m } } } } });

        var data = FormIssueDataPolicy.Read(schema, answered, [], bound, "es", new Dictionary<Guid, string>());

        Assert.Equal(new (string, string?)[] { ("items", null), ("pricesIncludeVat", "true"), ("other", "false") }, data.Select(datum => (datum.Key, datum.Value)));
        Assert.Equal("120.5", Assert.Single(Assert.Single(data[0].Items!)).Value);
        Assert.Equal(new (string, string?)[] { ("pricesIncludeVat", "true"), ("other", "false") },
            FormIssueDataPolicy.Read(schema, "[]", [], bound, "es", new Dictionary<Guid, string>()).Select(datum => (datum.Key, datum.Value)));
        Assert.Empty(FormIssueDataPolicy.Read(schema, "[]", [], new Dictionary<string, Guid> { ["items"] = Items }, "es", new Dictionary<Guid, string>()));
    }

    private static string? BoundPath(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.BoundQuestionChanged.Code, result.Error.Code);
        return (result.Error as ValidationError)?.Errors.Keys.Single();
    }
}
