using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// IV1-A: a library standard declares, by its code, the option a bound catalog question starts with («Alícuota» in 21 %). The copy
// keeps it in the question's source as the option's identity, like the rest of a bound question; the app loads it chosen.
public sealed class FormStandardDefaultTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Rate = Guid.CreateVersion7(), Notes = Guid.CreateVersion7(), Catalog = Guid.CreateVersion7(), Option = Guid.CreateVersion7();

    private static CanonicalFormField Field(Guid id, Guid? starts = null) => new(id, "field_" + id.ToString("N"), "choice",
        new Dictionary<string, string> { ["es"] = "Alícuota" }, null, false, null, null, new FormQuestionSource(Catalog, Default: starts));

    [Fact]
    public void The_option_a_question_starts_with_is_one_of_its_catalog_for_choosing_one()
    {
        static string[] Errors(string type, Guid starts)
        {
            var text = new Dictionary<string, string> { ["es"] = "Factura" };
            return new SaveFormDraftRequestValidator().Validate(new SaveFormDraftRequest(Guid.NewGuid(), text, "FAC", new(1, "es", text,
                [new FormFieldInput("rate", null, null, type, text, null, type == "multiChoice", null, null, new FormQuestionSource(Catalog, Default: starts),
                    Selection: type == "multiChoice" ? new(1, 2) : null)]))).Errors.Select(error => error.PropertyName).ToArray();
        }

        Assert.DoesNotContain("draft.fields[0].source.default", Errors("choice", Option));
        Assert.Contains("draft.fields[0].source.default", Errors("choice", Guid.Empty));
        Assert.Contains("draft.fields[0].source.default", Errors("multiChoice", Option));
    }

    [Fact]
    public void Only_a_bound_question_starts_with_an_option_and_it_does_not_change()
    {
        var bindings = FormDraftPolicy.Serialize(new Dictionary<string, Guid> { ["vatRate"] = Rate });
        Assert.True(FormStandardPolicy.CheckDeclared([Rate], [Field(Rate, Option), Field(Notes)]).IsSuccess);

        var unbound = FormStandardPolicy.CheckDeclared([Rate], [Field(Rate, Option), Field(Notes, Option)]);
        Assert.Equal(FormDraftErrors.QuestionConfigurationInvalid.Code, unbound.Error.Code);
        Assert.Equal("draft.fields[1].source.default", (unbound.Error as ValidationError)?.Errors.Keys.Single());
        var changed = FormStandardPolicy.CheckBound(bindings, [Field(Rate, Option)], [Field(Rate, Guid.CreateVersion7())]);
        Assert.Equal(FormDraftErrors.BoundQuestionChanged.Code, changed.Error.Code);
        Assert.Equal(FormDraftErrors.BoundQuestionChanged.Code, FormStandardPolicy.CheckBound(bindings, [Field(Rate, Option)], [Field(Rate)]).Error.Code);
    }

    [Fact]
    public async Task The_app_loads_the_option_a_question_starts_with_while_it_is_offered()
    {
        using var fixture = new FormEntryFixture();
        fixture.AddItem("21 %", 1);
        var reduced = fixture.AddItem("10,5 %", 2, "Reducida");
        var question = Assert.Single(FormDraftPolicy.Read<FormVersionSchema>(fixture.Catalogs.Version.SchemaJson).Questions);
        string Starting(Guid option) => FormDraftPolicy.Serialize(new FormVersionSchema(1, [question with { Source = question.Source! with { Default = option } }]));

        fixture.SchemaJson = Starting(reduced.Id);
        var catalog = Assert.Single((await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions).Catalog!;
        fixture.SchemaJson = Starting(Guid.CreateVersion7());
        var gone = Assert.Single((await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions).Catalog!;
        fixture.SchemaJson = null;
        var none = Assert.Single((await fixture.Service.GetAsync(fixture.Catalogs.DefinitionId, Ct)).Value.Questions).Catalog!;

        Assert.Equal(new FormEntryOptionResponse(reduced.Id, "10,5 %", "Reducida"), catalog.Default);
        Assert.Null(catalog.OnlyOption);
        Assert.Null(gone.Default);
        Assert.Null(none.Default);
        Assert.DoesNotContain("default", FormDraftPolicy.Serialize(none), StringComparison.Ordinal);
    }
}
