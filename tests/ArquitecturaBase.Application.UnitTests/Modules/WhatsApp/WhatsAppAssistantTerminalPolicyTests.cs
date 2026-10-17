using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantTerminalPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es", "La propuesta de catálogo está disponible en la web: ")]
    [InlineData("en", "The catalog proposal is available on the web: ")]
    public async Task Catalog_terminal_delegates_the_observed_proposal_and_returns_only_a_fixed_localized_ack(string culture, string expected)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = "forms.catalog-filters.propose", Culture = culture };
        var operation = host.Operation;
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, operation, Ct);
        Assert.True(result.IsSuccess); var response = Assert.IsType<WhatsAppAssistantTerminalResponse>(result.Value);
        Assert.Equal(expected + $"https://business.example.test/catalogos/{host.CatalogResult.Value.CatalogId:D}", response.Body);
        Assert.Equal(host.ProposalId, response.ProposalId); Assert.Equal(culture, response.Culture);
        Assert.Same(operation.Proposal, host.CatalogRequest!.Proposal); Assert.Same(operation.Provenance, host.CatalogRequest.Provenance);
        Assert.Equal(host.SubjectId, host.CatalogRequest.Context.ActorUserId); Assert.Equal(host.SubjectId, host.CatalogRequest.Context.SubjectUserId);
        Assert.Equal(1, host.CatalogCalls); Assert.Equal(0, host.MissingCalls);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Discarded")]
    public async Task A_recovered_catalog_receipt_does_not_claim_that_a_decided_proposal_is_still_inactive(string state)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = "forms.catalog-filters.propose" };
        host.CatalogResult = Result.Success(host.CatalogResult.Value with { State = state });
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct);
        Assert.True(result.IsSuccess); var response = Assert.IsType<WhatsAppAssistantTerminalResponse>(result.Value);
        Assert.Equal($"La propuesta de catálogo está disponible en la web: https://business.example.test/catalogos/{host.CatalogResult.Value.CatalogId:D}", response.Body);
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
        Assert.Equal(host.SubjectId, host.MissingRequest.Context.SubjectUserId); Assert.Equal(0, host.CatalogCalls); Assert.Equal(1, host.MissingCalls);
    }

    [Theory]
    [InlineData("forms.catalog-filters.propose")]
    [InlineData("forms.missing-request.propose")]
    public async Task Manual_terminal_still_calls_its_owner_but_never_returns_an_automatic_message(string capability)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = capability, Kind = WhatsAppAssistantRequestKind.Manual, Value = null };
        if (capability == "forms.catalog-filters.propose")
            host.CatalogResult = Result.Failure<ArquitecturaBase.Application.Models.StructuredData.StructuredCatalogProposalAcceptedResponse>(WhatsAppAssistantErrors.Forbidden);
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct);
        if (capability == "forms.catalog-filters.propose")
        { Assert.True(result.IsFailure); Assert.Equal(WhatsAppAssistantErrors.Forbidden, result.Error); }
        else
        { Assert.True(result.IsSuccess); var response = Assert.IsType<WhatsAppAssistantTerminalResponse>(result.Value); Assert.Null(response.Body); Assert.Equal(host.ProposalId, response.ProposalId); }
        Assert.Equal(1, host.CatalogCalls + host.MissingCalls);
        Assert.Equal(host.OperatorId, (host.CatalogRequest?.Context ?? host.MissingRequest!.Context).ActorUserId);
    }

    [Theory]
    [InlineData("forms.catalog-filters.propose")]
    [InlineData("forms.missing-request.propose")]
    public async Task Owner_failure_is_preserved_without_a_success_ack(string capability)
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = capability };
        host.CatalogResult = Result.Failure<ArquitecturaBase.Application.Models.StructuredData.StructuredCatalogProposalAcceptedResponse>(WhatsAppAssistantErrors.Forbidden);
        host.MissingResult = Result.Failure<ArquitecturaBase.Application.Models.StructuredData.StructuredMissingFormRequestCaptureResponse>(WhatsAppAssistantErrors.Forbidden);
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct);
        Assert.True(result.IsFailure); Assert.Equal(WhatsAppAssistantErrors.Forbidden, result.Error); Assert.Equal(1, host.CatalogCalls + host.MissingCalls);
    }

    [Theory]
    [InlineData("unknown", 1, "completed", true, true)]
    [InlineData("forms.missing-request.propose", 2, "completed", true, true)]
    [InlineData("forms.catalog-filters.propose", 2, "completed", true, true)]
    [InlineData("forms.missing-request.propose", 1, "running", true, true)]
    [InlineData("forms.missing-request.propose", 1, "completed", false, true)]
    [InlineData("forms.missing-request.propose", 1, "completed", true, false)]
    public async Task Other_or_incomplete_operations_are_not_terminal_effects(string capability, int version, string state, bool hasProposal, bool hasProvenance)
    {
        var host = new WhatsAppAssistantTerminalFixture(); var original = host.Operation;
        var operation = original with { State = state, Proposal = hasProposal ? original.Proposal! with { Capability = new(capability, version) } : null,
            Provenance = hasProvenance ? original.Provenance : null };
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, operation, Ct);
        Assert.True(result.IsSuccess); Assert.Null(result.Value); Assert.Equal(0, host.CatalogCalls + host.MissingCalls);
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
        Assert.True(result.IsFailure); Assert.Equal(WhatsAppAssistantErrors.InvalidProposal, result.Error); Assert.Equal(0, host.CatalogCalls + host.MissingCalls);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("channel")]
    public async Task A_crossed_context_is_rejected_before_an_owner_is_called(string mismatch)
    {
        var host = new WhatsAppAssistantTerminalFixture();
        var context = mismatch == "identity"
            ? host.Context with { ActorUserId = host.OperatorId, SubjectUserId = Guid.NewGuid() }
            : host.Context with { ChannelId = "whatsapp-manual" };
        var reference = host.Reference with { ContextJson = JsonSerializer.Serialize(context, JsonSerializerOptions.Web) };
        var result = await host.Policy.ApplyAsync(reference, host.Source, host.Operation, Ct);
        Assert.True(result.IsFailure); Assert.Equal(WhatsAppAssistantErrors.InvalidProposal, result.Error); Assert.Equal(0, host.CatalogCalls + host.MissingCalls);
    }

    [Fact]
    public async Task Automatic_catalog_requires_the_public_web_origin_before_accepting_a_draft()
    {
        var host = new WhatsAppAssistantTerminalFixture { CapabilityId = "forms.catalog-filters.propose", Value = null };
        var result = await host.Policy.ApplyAsync(host.Reference, host.Source, host.Operation, Ct);
        Assert.True(result.IsFailure); Assert.Equal(WhatsAppAssistantErrors.Unavailable, result.Error); Assert.Equal(0, host.CatalogCalls);
    }

    [Fact]
    public void Terminal_response_does_not_format_its_message_content()
    {
        var response = new WhatsAppAssistantTerminalResponse(Guid.NewGuid(), "private message", "es");
        Assert.DoesNotContain("private message", response.ToString());
    }
}
