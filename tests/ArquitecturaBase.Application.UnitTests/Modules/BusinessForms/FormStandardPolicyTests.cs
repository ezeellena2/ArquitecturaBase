using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// BI1-A: a bound question keeps everything but its text, help and place; the bindings go from each datum to its field id.
public sealed class FormStandardPolicyTests
{
    private static readonly Guid Customer = Guid.CreateVersion7(), Kind = Guid.CreateVersion7(), Notes = Guid.CreateVersion7();
    private static readonly string Bindings = FormDraftPolicy.Serialize(new Dictionary<string, Guid> { ["customer"] = Customer, ["kind"] = Kind });

    private static CanonicalFormField Field(Guid id, string type, string label, bool required = true, IReadOnlyList<CanonicalFormChoice>? options = null) =>
        new(id, "field_" + id.ToString("N"), type, new Dictionary<string, string> { ["es"] = label }, null, required, type == "shortText" ? 80 : null, options);

    private static readonly CanonicalFormChoice Goods = new(Guid.CreateVersion7(), new Dictionary<string, string> { ["es"] = "Productos" });
    private static readonly CanonicalFormChoice Services = new(Guid.CreateVersion7(), new Dictionary<string, string> { ["es"] = "Servicios" });

    private static readonly CanonicalFormField[] Saved =
        [Field(Customer, "shortText", "Cliente"), Field(Kind, "choice", "Concepto", options: [Goods, Services]), Field(Notes, "longText", "Notas", false)];

    [Fact]
    public void Text_help_and_place_change_and_an_unbound_question_goes() =>
        Assert.True(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[1] with { Label = new Dictionary<string, string> { ["es"] = "Tipo" },
            Options = [Goods with { Label = new Dictionary<string, string> { ["es"] = "Bienes" } }, Services] },
            Saved[0] with { Help = new Dictionary<string, string> { ["es"] = "Razón social" } }]).IsSuccess);

    [Fact]
    public void A_bound_question_cannot_go_nor_change_type_requirement_or_options()
    {
        Assert.Equal("draft.fields", Path(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[1], Saved[2]])));
        Assert.Equal("draft.fields[0]", Path(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[0] with { Required = false }, Saved[1]])));
        Assert.Equal("draft.fields[1]", Path(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[0], Saved[1] with { Options = [Goods] }])));
        Assert.Equal("draft.fields[0]", Path(FormStandardPolicy.CheckBound(Bindings, Saved, [Saved[0] with { MaxLength = 120 }, Saved[1]])));
    }

    [Fact]
    public void The_bindings_name_each_datum_by_its_new_field_id()
    {
        var map = new FormIdentityMap([new("local-customer", Customer, "field_a", null), new("local-kind", Kind, "field_b", null)], []);
        var bindings = FormStandardPolicy.Bindings(new("test.invoice", 1, new Dictionary<string, string> { ["local-customer"] = "customer", ["local-kind"] = "kind" }), map);
        Assert.Equal(new Dictionary<string, Guid> { ["customer"] = Customer, ["kind"] = Kind }, bindings);
    }

    private static string? Path(ArquitecturaBase.Domain.Results.Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.BoundQuestionChanged.Code, result.Error.Code);
        return (result.Error as ArquitecturaBase.Domain.Results.ValidationError)?.Errors.Keys.Single();
    }

    [Fact]
    public void The_suggested_prefix_is_the_standards_or_the_first_with_a_number_that_no_form_uses()
    {
        Assert.Equal("FAC", FormStandardPolicy.SuggestedPrefix("FAC", new HashSet<string>()));
        Assert.Equal("FAC3", FormStandardPolicy.SuggestedPrefix("FAC", new HashSet<string> { "FAC", "FAC2" }));
        Assert.Equal("FAC2", FormStandardPolicy.SuggestedPrefix("FAC", new HashSet<string> { "FAC", "FAC3" }));
        // Within the twelve characters of a prefix, the number takes the place of the last letters.
        Assert.Equal("ABCDEFGHIJK2", FormStandardPolicy.SuggestedPrefix("ABCDEFGHIJKL", new HashSet<string> { "ABCDEFGHIJKL" }));
    }
}
