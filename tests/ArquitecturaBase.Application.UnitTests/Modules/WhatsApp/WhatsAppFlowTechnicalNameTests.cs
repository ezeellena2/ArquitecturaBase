using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// The name of a Flow in Meta is what its official preview and WhatsApp Manager show (2026-11-06): the form's name, its version and
// language, and the end of the publication's id, which keeps it unique in the account. The app later verifies the Flow by that exact
// name, so it fits the 80 characters it is stored with and only keeps characters Meta has no reason to change.
public sealed class WhatsAppFlowTechnicalNameTests
{
    private static readonly Guid Operation = Guid.Parse("01a1b12a-2e03-7df0-8286-b183ab63a1f9");

    [Fact]
    public void A_flow_is_named_after_its_form_version_and_language()
    {
        Assert.Equal("Prueba renglones 1 · v2 · es · ab63a1f9", WhatsAppFlowTechnicalName.For(Names("Prueba renglones 1"), "es", 2, "es", Operation));
    }

    [Fact]
    public void Each_language_takes_its_own_name_or_else_the_original_one()
    {
        var names = new Dictionary<string, string> { ["es"] = "Visitas", ["en"] = "Visits" };
        Assert.Equal("Visits · v3 · en · ab63a1f9", WhatsAppFlowTechnicalName.For(names, "es", 3, "en", Operation));
        Assert.Equal("Visitas · v3 · en · ab63a1f9", WhatsAppFlowTechnicalName.For(Names("Visitas"), "es", 3, "en", Operation));
    }

    [Fact]
    public void A_long_name_is_cut_so_the_whole_name_fits()
    {
        var name = WhatsAppFlowTechnicalName.For(Names(new string('a', 90)), "es", 12, "es", Operation);
        Assert.Equal(WhatsAppFlowTechnicalName.MaxLength, name.Length);
        Assert.Equal(new string('a', 57) + "… · v12 · es · ab63a1f9", name);
    }

    [Fact]
    public void Spaces_are_collapsed_and_characters_Meta_may_change_are_left_out()
    {
        Assert.Equal("Visita técnica Nº 3 (campo) · v1 · es · ab63a1f9",
            WhatsAppFlowTechnicalName.For(Names("  Visita\ttécnica 😀 Nº 3\n(campo)​ "), "es", 1, "es", Operation));
        Assert.Equal("Visita técnica · v1 · es · ab63a1f9", WhatsAppFlowTechnicalName.For(Names("Visita técnica"), "es", 1, "es", Operation));
    }

    [Fact]
    public void A_name_with_nothing_left_and_a_request_without_version_still_give_a_unique_name()
    {
        Assert.Equal("form · v1 · es · ab63a1f9", WhatsAppFlowTechnicalName.For(Names("😀 ✅"), "es", 1, "es", Operation));
        Assert.Equal("Visitas · es · ab63a1f9", WhatsAppFlowTechnicalName.For(Names("Visitas"), "es", null, "es", Operation));
    }

    [Fact]
    public async Task Acceptance_creates_each_flow_with_the_name_of_its_form()
    {
        var fixture = new WhatsAppFlowPublicationFixture();
        var text = new Dictionary<string, string> { ["es"] = "Prueba renglones 1" };
        var snapshot = new StructuredFormCheckRequest(1, "es", text, text, [], ["es"]);
        var compilation = WhatsAppFlowCompiler.Compile(snapshot);
        var request = new StructuredFormPublicationRequest(Operation, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ["forms.read"], "publication",
            WhatsAppFlowCompiler.ProfileRevision, compilation.Flows.Select(flow => new StructuredFormPublicationCulture(
                flow.Culture, WhatsAppFlowPublicationPolicy.CompilationFingerprint(flow.Hash))).ToArray(), snapshot, VersionNumber: 2);

        var result = await fixture.CreateService().AcceptAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Prueba renglones 1 · v2 · es · ab63a1f9", Assert.Single(fixture.Bindings).TechnicalName);
        // The version only names the Flow: the request's fingerprint stays as it was, so a replay of an older request still matches.
        Assert.Equal(WhatsAppFlowPublicationPolicy.RequestFingerprint(request with { VersionNumber = null }), WhatsAppFlowPublicationPolicy.RequestFingerprint(request));
        Assert.Equal(WhatsAppFlowPublicationPolicy.Hash(request with { VersionNumber = null }), WhatsAppFlowPublicationPolicy.Hash(request));
    }

    private static Dictionary<string, string> Names(string original) => new() { ["es"] = original };
}
