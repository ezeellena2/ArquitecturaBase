using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// IA3-A: what a channel shows of an authoring proposal, in the person's language.
public sealed class FormAuthoringSummaryPolicyTests
{
    private static FormAuthoringProposedQuestion Question(string label, string type, FormAuthoringProposedCatalog? catalog = null) =>
        new(label, type, false, [], catalog);

    [Fact]
    public void A_form_shows_its_name_and_prefix_and_up_to_ten_numbered_questions_with_their_type()
    {
        var questions = new[] { Question("Fecha", "date"), Question("Milímetros", "number"), Question("Observaciones", "longText") }
            .Concat(Enumerable.Range(4, 9).Select(index => Question($"Dato {index}", "shortText"))).ToArray();
        var summary = FormAuthoringSummaryPolicy.Form(new("es", "Parte de lluvias", "LLU", "Enviar", questions), "es");
        Assert.Equal(StructuredAuthoringKind.Form, summary.Kind);
        Assert.Equal("Parte de lluvias · LLU", summary.Title);
        Assert.Equal(10, summary.Lines.Count);
        Assert.Equal(["1. Fecha · fecha", "2. Milímetros · número", "3. Observaciones · texto largo"], summary.Lines.Take(3));
        Assert.Equal("10. Dato 10 · texto corto", summary.Lines[9]);
        Assert.Equal(2, summary.MoreCount);
    }

    [Fact]
    public void The_types_follow_the_person_language_and_a_catalog_question_names_its_catalog()
    {
        var proposal = new FormAuthoringFormProposal("es", "Recepción", "REC", "Enviar", [Question("Proveedor", "catalog", new(Guid.NewGuid(), "Proveedores")),
            Question("¿Llegó entera?", "yesNo"), Question("Estado", "choice"), Question("Fallas", "multiChoice"), Question("Foto", "photo"), Question("Remito", "document")]);
        Assert.Equal(["1. Proveedor · elegir de Proveedores", "2. ¿Llegó entera? · sí o no", "3. Estado · elegir una", "4. Fallas · elegir varias",
            "5. Foto · foto", "6. Remito · documento"], FormAuthoringSummaryPolicy.Form(proposal, "es").Lines);
        Assert.Equal(["1. Proveedor · choose from Proveedores", "2. ¿Llegó entera? · yes or no", "3. Estado · choose one", "4. Fallas · choose several",
            "5. Foto · photo", "6. Remito · document"], FormAuthoringSummaryPolicy.Form(proposal, "en").Lines);
        Assert.Equal(0, FormAuthoringSummaryPolicy.Form(proposal, "es").MoreCount);
    }

    [Fact]
    public void A_manual_catalog_says_in_one_line_what_the_code_and_each_option_are_and_its_fields()
    {
        var proposal = new FormAuthoringCatalogProposal("es", "Contratistas", "Empresas que trabajan en el campo.", "text", "el CUIT", "la razón social",
            [new("Rubro", "text"), new("Zona", "text")]);
        var summary = FormAuthoringSummaryPolicy.Catalog(proposal, "es");
        Assert.Equal(StructuredAuthoringKind.Catalog, summary.Kind);
        Assert.Equal("Contratistas", summary.Title);
        Assert.Equal(["el código es el CUIT, cada opción la razón social y los campos Rubro y Zona"], summary.Lines);
        Assert.Equal(["the code is el CUIT, each option is la razón social and the fields Rubro and Zona"], FormAuthoringSummaryPolicy.Catalog(proposal, "en").Lines);
        Assert.Equal(["el código es el CUIT, cada opción la razón social y el campo Rubro"],
            FormAuthoringSummaryPolicy.Catalog(proposal with { Fields = [new("Rubro", "text")] }, "es").Lines);
        Assert.Equal(["el código es el CUIT y cada opción la razón social"], FormAuthoringSummaryPolicy.Catalog(proposal with { Fields = [] }, "es").Lines);
    }
}
