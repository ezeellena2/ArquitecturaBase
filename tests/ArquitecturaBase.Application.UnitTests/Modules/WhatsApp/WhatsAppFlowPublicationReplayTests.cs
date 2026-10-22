using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowPublicationReplayTests
{
    [Fact]
    public async Task Fresh_acceptance_protects_the_compiled_multiple_selection_range_in_each_culture_projection()
    {
        var fixture = new WhatsAppFlowPublicationFixture();
        var texts = new Dictionary<string, string> { ["es"] = "Riesgos", ["en"] = "Risks" };
        var fieldId = Guid.CreateVersion7();
        var choices = Enumerable.Range(1, 3).Select(index => new StructuredFormChoice(Guid.CreateVersion7(),
            new Dictionary<string, string> { ["es"] = "Opción " + index, ["en"] = "Option " + index })).ToArray();
        var question = new StructuredFormQuestion(fieldId, "field_" + fieldId.ToString("N"), "multiChoice", texts,
            null, true, null, choices, Selection: new(1, 3));
        var snapshot = new StructuredFormCheckRequest(1, "es", texts,
            new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" }, [question], ["es", "en"]);
        // These are the real check fingerprints sent to Accept; the assertions inspect only the
        // projections that the service itself passed to its protector after compiling the request.
        var check = WhatsAppFlowCompiler.Compile(snapshot);
        var request = new StructuredFormPublicationRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), ["forms.read"], new string('b', 64), WhatsAppFlowCompiler.ProfileRevision,
            check.Flows.Select(flow => new StructuredFormPublicationCulture(flow.Culture,
                WhatsAppFlowPublicationPolicy.CompilationFingerprint(flow.Hash))).ToArray(), snapshot);

        var result = await fixture.CreateService().AcceptAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Accepted", result.Value.State);
        Assert.Equal(new[] { "en", "es" }, fixture.Projections.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, fixture.Bindings.Count);
        foreach (var (culture, projection) in fixture.Projections)
        {
            Assert.Equal(culture, projection.Culture);
            var mapping = Assert.Single(projection.Mapping);
            Assert.Equal(fieldId, mapping.FieldId);
            Assert.Equal("multiChoice", mapping.Type);
            Assert.Equal(choices.Select(choice => choice.ChoiceId), mapping.Choices);
            var json = JsonSerializer.SerializeToElement(mapping, JsonSerializerOptions.Web);
            var selection = json.GetProperty("selection");
            Assert.Equal(1, selection.GetProperty("minSelected").GetInt32());
            Assert.Equal(3, selection.GetProperty("maxSelected").GetInt32());
        }
        Assert.Equal(1, fixture.AccountChecks);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_previous_publication_rejects_a_changed_selection_range_even_when_its_culture_fingerprints_are_reused()
    {
        var fixture = new WhatsAppFlowPublicationFixture();
        var texts = new Dictionary<string, string> { ["es"] = "Original" };
        var fieldId = Guid.CreateVersion7();
        var question = new StructuredFormQuestion(fieldId, "field_" + fieldId.ToString("N"), "multiChoice", texts,
            null, true, null, Enumerable.Range(1, 3).Select(index => new StructuredFormChoice(Guid.CreateVersion7(),
                new Dictionary<string, string> { ["es"] = "Opción " + index })).ToArray(), Selection: new(1, 3));
        var snapshot = new StructuredFormCheckRequest(1, "es", texts, texts, [question], ["es"]);
        var request = new StructuredFormPublicationRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), ["forms.read"], new string('b', 64), WhatsAppFlowCompiler.ProfileRevision,
            [new("es", new string('c', 64))], snapshot);
        var account = new WhatsAppFlowAccountContext("waba", "phone", "v25.0", "configuration");
        // The stored contract uses the original range's real request fingerprint. Culture fingerprints
        // are the prior sealed values, reused exactly by the replay; they must not authorize a new range.
        var manifest = new WhatsAppFlowStoredManifest(2, WhatsAppFlowPublicationPolicy.RequestFingerprint(request),
            request.PublicationFingerprint, account, request.RequiredPermissions);
        fixture.PreviousManifest = manifest;
        fixture.Previous = new(Guid.CreateVersion7(), request.OperationId, request.DefinitionId, request.VersionId,
            request.ActorUserId, [1], null,
            [new(Guid.CreateVersion7(), "es", "previous_flow", [2], null, "Accepted", "Create", null, "[]")]);
        var changed = request with { Snapshot = snapshot with { Questions = [question with { Selection = new(1, 2) }] } };
        Assert.Equal(request.OperationId, changed.OperationId);
        Assert.Same(request.Cultures, changed.Cultures);

        var result = await fixture.CreateService().AcceptAsync(changed, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(FlowErrors.PayloadMismatch, result.Error);
        Assert.Equal(0, fixture.AccountChecks);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
        Assert.Empty(fixture.Bindings);
        Assert.Empty(fixture.Projections);
        Assert.Same(manifest, fixture.PreviousManifest);
        Assert.Equal(new StructuredFormSelectionSettings(1, 3), question.Selection);
    }
}
