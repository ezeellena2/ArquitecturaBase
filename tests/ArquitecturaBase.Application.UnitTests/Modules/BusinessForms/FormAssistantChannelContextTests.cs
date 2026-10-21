using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAssistantChannelContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("fixture-assistant")]
    [InlineData("another-owner")]
    public async Task The_assistant_can_read_its_own_capture_without_interpreting_the_channels_opaque_id(string channelId)
    {
        var f = new FormSuggestionAssistantFixture();
        var context = f.Context with { ChannelId = channelId, InteractionMode = AssistantInteractionMode.Assistant };
        Assert.DoesNotContain(BusinessFormsPermissions.RecordsRead, f.Runtime.Assistant.Permissions.Permissions[context.ActorUserId]);

        var result = await f.Source.ExecuteAsync(new(f.TurnId, context,
            new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1), "{}", [f.Offers()]), Ct);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(Assert.IsType<FormSuggestionAssistantCandidatesPayload>(result.Value.Payload).SourceValues);
    }

    [Theory]
    [InlineData("fixture-assistant")]
    [InlineData("another-owner")]
    public async Task A_catalog_proposal_preserves_opaque_channel_provenance_without_interpreting_its_id(string channelId)
    {
        var f = new FormCatalogProposalFixture();
        var request = f.Request();
        request = request with
        {
            Context = request.Context with { ChannelId = channelId, InteractionMode = AssistantInteractionMode.Assistant },
            Provenance = request.Provenance with { ChannelId = channelId }
        };

        var result = await f.Service.AcceptAsync(request, Ct);

        Assert.True(result.IsSuccess);
        var catalog = Assert.Single(f.Catalogs);
        var evidence = JsonSerializer.Deserialize<FormCatalogProposalEvidence>(catalog.ProposalEvidenceJson!, JsonSerializerOptions.Web)!;
        Assert.Equal(request.Provenance, evidence.Provenance);
        Assert.False(catalog.IsActive);
        Assert.Equal(1, f.Uow.Commits);
    }

    [Theory]
    [InlineData("fixture-assistant")]
    [InlineData("another-owner")]
    public async Task A_missing_form_request_uses_the_trusted_context_without_interpreting_the_channels_opaque_id(string channelId)
    {
        var f = new FormMissingRequestFixture();
        var request = f.Request();
        request = request with
        {
            Context = request.Context with { ChannelId = channelId, InteractionMode = AssistantInteractionMode.Assistant },
            Provenance = request.Provenance with { ChannelId = channelId }
        };

        var result = await f.Service.CaptureAsync(request, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(f.Assistant.UserId, Assert.Single(f.Requests).SubjectUserId);
        Assert.Single(f.Topics);
        Assert.Equal(1, f.Uow.Commits);
    }
    [Theory]
    [InlineData(AssistantInteractionMode.Manual)]
    [InlineData(AssistantInteractionMode.Unspecified)]
    [InlineData((AssistantInteractionMode)99)]
    public async Task A_private_channel_id_does_not_grant_the_automatic_history_exception(AssistantInteractionMode mode)
    {
        var f = new FormSuggestionAssistantFixture();
        var context = f.Context with { InteractionMode = mode };
        var result = await f.Source.ExecuteAsync(new(f.TurnId, context,
            new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1), "{}", [f.Offers()]), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, f.Runtime.Responses.Reads);
    }

    [Theory]
    [InlineData(AssistantInteractionMode.Manual)]
    [InlineData(AssistantInteractionMode.Unspecified)]
    [InlineData((AssistantInteractionMode)99)]
    public async Task A_private_channel_id_does_not_authorize_a_catalog_outside_assistant_mode(AssistantInteractionMode mode)
    {
        var f = new FormCatalogProposalFixture();
        var request = f.Request();
        var result = await f.Service.AcceptAsync(request with { Context = request.Context with { InteractionMode = mode } }, Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(FormCatalogErrors.ContentInvalid, result.Error);
        Assert.Empty(f.Catalogs);
        Assert.Equal(0, f.PreviewReads);
        Assert.Equal(0, f.Uow.Transactions);
    }

    [Theory]
    [InlineData(AssistantInteractionMode.Unspecified)]
    [InlineData((AssistantInteractionMode)99)]
    public async Task A_missing_request_requires_an_explicit_known_interaction_mode(AssistantInteractionMode mode)
    {
        var f = new FormMissingRequestFixture();
        var request = f.Request();
        var result = await f.Service.CaptureAsync(request with { Context = request.Context with { InteractionMode = mode } }, Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(FormMissingRequestErrors.ContentInvalid, result.Error);
        Assert.Empty(f.Requests);
        Assert.Equal(0, f.Uow.Transactions);
    }

    [Fact]
    public async Task Manual_analysis_uses_the_real_operator_without_interpreting_the_opaque_channel_id()
    {
        var f = new FormMissingRequestFixture();
        var request = f.Request();
        request = request with
        {
            Context = request.Context with { ChannelId = "fixture-review", ActorUserId = f.Manager, InteractionMode = AssistantInteractionMode.Manual },
            Provenance = request.Provenance with { ChannelId = "fixture-review" }
        };
        var result = await f.Service.CaptureAsync(request, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(f.Assistant.UserId, Assert.Single(f.Requests).SubjectUserId);
        Assert.Equal(1, f.Uow.Commits);
    }
}
