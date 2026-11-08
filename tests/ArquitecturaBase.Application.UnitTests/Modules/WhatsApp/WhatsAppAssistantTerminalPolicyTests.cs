using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantTerminalPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es", "Te propongo este formulario:\n*Parte de lluvias · LLU*\n1. Milímetros · número\ny 3 preguntas más\n¿Lo armo?", "Crear borrador", "Cambiar algo")]
    [InlineData("en", "I propose this form:\n*Parte de lluvias · LLU*\n1. Milímetros · número\nand 3 more questions\nShall I build it?", "Create draft", "Change something")]
    public async Task A_form_proposal_is_summarized_with_its_two_buttons_and_creates_nothing(string culture, string body, string create, string change)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = "forms.authoring.propose-form", Culture = culture };
        host.Summary = host.Summary! with { MoreCount = 3 };
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct);
        var response = Assert.IsType<WhatsAppAssistantTerminalResponse>(result.Value);
        Assert.Equal((body, host.ProposalId, culture), (response.Body, response.ProposalId, response.Culture));
        Assert.Equal([create, change], response.Buttons!.Select(button => button.Title));
        Assert.Equal([WhatsAppAssistantAuthoringReplyCodec.Encode(host.RequestId, host.ProposalId, WhatsAppAssistantAuthoringReplyCodec.Create),
            WhatsAppAssistantAuthoringReplyCodec.Encode(host.RequestId, host.ProposalId, WhatsAppAssistantAuthoringReplyCodec.Change)], response.Buttons!.Select(button => button.Id));
        Assert.Equal((1, 0), (host.SummaryCalls, host.MissingCalls));
    }

    [Theory]
    [InlineData("forms.authoring.propose-catalog")]
    [InlineData("forms.catalog-filters.propose")]
    public async Task A_catalog_proposal_also_from_H4_is_one_sentence_with_create_catalog(string capability)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = capability,
            Summary = new(StructuredAuthoringKind.Catalog, "Contratistas", ["el código es el CUIT, cada opción la razón social y el campo Rubro"], 0) };
        var response = Assert.IsType<WhatsAppAssistantTerminalResponse>((await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct)).Value);
        Assert.Equal("Te propongo el catálogo Contratistas: el código es el CUIT, cada opción la razón social y el campo Rubro. ¿Lo armo?", response.Body);
        Assert.Equal(["Crear catálogo", "Cambiar algo"], response.Buttons!.Select(button => button.Title));
    }

    [Fact]
    public async Task Questions_that_do_not_fit_one_message_join_the_ones_counted_at_the_end()
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = "forms.authoring.propose-form",
            Summary = new(StructuredAuthoringKind.Form, "Inspección · INS", Enumerable.Range(1, 10).Select(index => $"{index}. {new string('x', 150)} · texto corto").ToArray(), 2) };
        var response = Assert.IsType<WhatsAppAssistantTerminalResponse>((await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct)).Value);
        Assert.True(response.Body!.Length <= 1024);
        var shown = response.Body.Split('\n').Count(line => line.EndsWith("· texto corto", StringComparison.Ordinal));
        Assert.EndsWith($"y {12 - shown} preguntas más\n¿Lo armo?", response.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("noPermission", null, "Tu cuenta no puede armar formularios ni catálogos. Pedíselo a quien administra el sistema.")]
    [InlineData("formExists", "/formularios/0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d6f", "Por ahora no cambio formularios que ya existen. Lo podés cambiar desde la app: https://business.example.test/formularios/0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d6f")]
    [InlineData("catalogExists", "/catalogos/0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d70", "Por ahora no cambio catálogos que ya existen. Lo podés cambiar desde la app: https://business.example.test/catalogos/0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d70")]
    public async Task Why_nothing_can_be_built_is_a_fixed_text_with_the_link_to_what_exists(string reason, string? path, string expected)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = "forms.authoring.decline",
            Summary = new(StructuredAuthoringKind.Declined, "", [], 0) { Reason = reason, AppPath = path } };
        var response = Assert.IsType<WhatsAppAssistantTerminalResponse>((await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct)).Value);
        Assert.Equal(expected, response.Body);
        Assert.Null(response.Buttons);
        Assert.False(response.AwaitsAnswer);
    }

    // «Pregunta y espera»: the channel never says the model's words, so it asks with a fixed text and the next message answers.
    [Theory]
    [InlineData("es", "No me queda claro qué querés que arme. Contame si es un formulario o un catálogo, para qué es y qué datos lleva, por escrito o con un audio.")]
    [InlineData("en", "I am not sure what you want me to build. Tell me whether it is a form or a catalog, what it is for and what information it holds, in writing or with an audio.")]
    public async Task An_unclear_request_asks_what_to_build_with_a_fixed_text_and_waits_for_the_answer(string culture, string expected)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = "forms.authoring.decline", Culture = culture, Value = null,
            Summary = new(StructuredAuthoringKind.Declined, "", [], 0) { Reason = "unclear" } };
        var response = Assert.IsType<WhatsAppAssistantTerminalResponse>((await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct)).Value);
        Assert.Equal((expected, true, host.ProposalId), (response.Body, response.AwaitsAnswer, response.ProposalId));
        Assert.Null(response.Buttons);
    }

    [Fact]
    public async Task A_link_needs_the_public_web_origin_and_a_summary_the_installed_owner()
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = "forms.authoring.decline", Value = null,
            Summary = new(StructuredAuthoringKind.Declined, "Parte de lluvias", [], 0) { Reason = "formExists", AppPath = "/formularios/x" } };
        Assert.Equal(WhatsAppAssistantErrors.Unavailable, (await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct)).Error);
        host.Summary = null;
        Assert.Equal(WhatsAppAssistantErrors.Unavailable, (await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct)).Error);
    }

    [Theory]
    [InlineData("es", false, "No hay un formulario para eso. Registramos el pedido para que lo revisen.")]
    [InlineData("es", true, "No hay un formulario para eso. Registramos el pedido para que lo revisen.")]
    [InlineData("en", false, "There is no form for that. We recorded the request for review.")]
    [InlineData("en", true, "There is no form for that. We recorded the request for review.")]
    public async Task Missing_terminal_preserves_the_source_receipt_and_original_time_and_acknowledges_a_recovered_receipt(string culture, bool replay, string expected)
    {
        var host = new WhatsAppAssistantTerminalFixture { Culture = culture };
        host.MissingResult = Result.Success(host.MissingResult.Value with { WasAlreadyRecorded = replay });
        var operation = host.Operation;
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, operation, Ct);
        Assert.True(result.IsSuccess); var response = Assert.IsType<WhatsAppAssistantTerminalResponse>(result.Value);
        Assert.Equal(expected, response.Body); Assert.Equal(culture, response.Culture); Assert.Equal(host.ProposalId, response.ProposalId);
        Assert.Equal(host.SourceId, host.MissingRequest!.SourceReceiptId); Assert.Equal(host.OccurredAtUtc, host.MissingRequest.RequestedAtUtc);
        Assert.Same(operation.Proposal, host.MissingRequest.Proposal); Assert.Same(operation.Provenance, host.MissingRequest.Provenance);
        Assert.Equal(host.SubjectId, host.MissingRequest.Context.SubjectUserId); Assert.Equal((0, 1), (host.SummaryCalls, host.MissingCalls));
    }

    [Theory]
    [InlineData("forms.catalog-filters.propose")]
    [InlineData("forms.missing-request.propose")]
    public async Task Manual_never_returns_an_automatic_message_and_only_a_missing_request_reaches_its_owner(string capability)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = capability, Kind = WhatsAppAssistantRequestKind.Manual, Value = null };
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct);
        var response = Assert.IsType<WhatsAppAssistantTerminalResponse>(result.Value);
        Assert.Null(response.Body); Assert.Null(response.Buttons); Assert.Equal(host.ProposalId, response.ProposalId);
        Assert.Equal(capability == "forms.missing-request.propose" ? 1 : 0, host.MissingCalls);
        Assert.Equal(0, host.SummaryCalls);
    }

    [Fact]
    public async Task Owner_failure_is_preserved_without_a_success_ack()
    {
        var host = new WhatsAppAssistantTerminalFixture();
        host.MissingResult = Result.Failure<StructuredMissingFormRequestCaptureResponse>(WhatsAppAssistantErrors.Forbidden);
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct);
        Assert.True(result.IsFailure); Assert.Equal(WhatsAppAssistantErrors.Forbidden, result.Error); Assert.Equal(1, host.MissingCalls);
    }

    [Theory]
    [InlineData("unknown", 1, "completed", true, true)]
    [InlineData("forms.missing-request.propose", 2, "completed", true, true)]
    [InlineData("forms.catalog-filters.propose", 2, "completed", true, true)]
    [InlineData("forms.authoring.propose-form", 2, "completed", true, true)]
    [InlineData("forms.missing-request.propose", 1, "running", true, true)]
    [InlineData("forms.missing-request.propose", 1, "completed", false, true)]
    [InlineData("forms.missing-request.propose", 1, "completed", true, false)]
    public async Task Other_or_incomplete_operations_are_not_terminal_effects(string capability, int version, string state, bool hasProposal, bool hasProvenance)
    {
        var host = new WhatsAppAssistantTerminalFixture(); var original = host.Operation;
        var operation = original with { State = state, Proposal = hasProposal ? original.Proposal! with { Capability = new(capability, version) } : null,
            Provenance = hasProvenance ? original.Provenance : null };
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, operation, Ct);
        Assert.True(result.IsSuccess); Assert.Null(result.Value); Assert.Equal(0, host.SummaryCalls + host.MissingCalls);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("source")]
    [InlineData("contact")]
    public async Task Cross_reference_inputs_are_rejected_before_an_owner_is_called(string mismatch)
    {
        var host = new WhatsAppAssistantTerminalFixture(); var operation = host.Operation; var source = host.Source;
        if (mismatch == "request") operation = operation with { RequestId = Guid.NewGuid() };
        if (mismatch == "source") source = source with { Id = Guid.NewGuid() };
        if (mismatch == "contact") source = source with { ContactId = Guid.NewGuid() };
        var result = await host.Policy.ApplyAsync(host.Reference, source, operation, Ct);
        Assert.True(result.IsFailure); Assert.Equal(WhatsAppAssistantErrors.InvalidProposal, result.Error); Assert.Equal(0, host.SummaryCalls + host.MissingCalls);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("channel")]
    [InlineData("mode")]
    public async Task A_crossed_context_is_rejected_before_an_owner_is_called(string mismatch)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = "forms.authoring.propose-form" };
        var context = mismatch switch
        {
            "identity" => host.Context with { ActorUserId = host.OperatorId, SubjectUserId = Guid.NewGuid() },
            "mode" => host.Context with { InteractionMode = AssistantInteractionMode.Manual },
            _ => host.Context with { ChannelId = "whatsapp-manual" }
        };
        var reference = host.Reference with { ContextJson = JsonSerializer.Serialize(context, JsonSerializerOptions.Web) };
        var result = await host.Policy.ApplyAsync(reference, host.Source, host.Operation, Ct);
        Assert.True(result.IsFailure); Assert.Equal(WhatsAppAssistantErrors.InvalidProposal, result.Error); Assert.Equal(0, host.SummaryCalls + host.MissingCalls);
    }

    [Fact]
    public void Terminal_response_does_not_format_its_message_content()
    {
        var response = new WhatsAppAssistantTerminalResponse(Guid.NewGuid(), "private message", "es");
        Assert.DoesNotContain("private message", response.ToString());
    }
}
