using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// SA1-A: «Dónde se completa» belongs to the draft. «Sólo en la app» travels as true; the other value is omitted, so the drafts,
// responses and fingerprints of every form that existed keep their bytes.
public sealed class FormAppOnlyDraftTests
{
    private static readonly IReadOnlyDictionary<string, string> Name = new Dictionary<string, string> { ["es"] = "Remito" };
    private static readonly IReadOnlyDictionary<string, string> Submit = new Dictionary<string, string> { ["es"] = "Enviar" };

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, null)]
    [InlineData(null, null)]
    public void Only_in_the_app_is_kept_as_true_and_the_other_value_is_omitted(bool? sent, bool? kept)
    {
        var normalized = FormDraftPolicy.Normalize(Request(sent));

        Assert.Equal(kept, normalized.Draft.AppOnly);
        Assert.Equal(kept is not null, FormDraftPolicy.Serialize(normalized.Draft).Contains("\"appOnly\"", StringComparison.Ordinal));
    }

    [Fact]
    public void The_saved_draft_says_it_is_completed_only_in_the_app()
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Remito", FormDraftPolicy.Serialize(Name), "REM", "es", FormDraftPolicy.Serialize(Submit),
            FormDraftPolicy.Serialize(new FormQuestionSchema(1, [])), 0, true, appOnly: true).IsSuccess);
        Assert.True(FormDraftPolicy.Definition(definition).Draft.AppOnly);

        Assert.True(definition.SetDraft(1, "Remito", FormDraftPolicy.Serialize(Name), "REM", "es", FormDraftPolicy.Serialize(Submit),
            definition.SchemaJson, 0, false).IsSuccess);
        var whatsApp = FormDraftPolicy.Definition(definition);
        Assert.Null(whatsApp.Draft.AppOnly);
        Assert.DoesNotContain("appOnly", FormDraftPolicy.Serialize(whatsApp), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(null, null, true)]
    [InlineData(true, null, false)]
    [InlineData(null, true, false)]
    public void A_check_or_acceptance_matches_only_a_draft_completed_in_the_same_place(bool? sent, bool? saved, bool matches)
    {
        var stored = new FormDefinitionResponse(Guid.CreateVersion7(), Name, "REM", false, 1, new FormDraftResponse(1, "es", Submit, [], null, saved));

        Assert.Equal(matches, FormPublicationSnapshotPolicy.Matches(FormDraftPolicy.Normalize(Request(sent)), stored));
    }

    [Fact]
    public void The_front_learns_that_a_form_channel_is_installed()
    {
        Assert.Contains("form-channels", Features(withChannel: true));
        Assert.DoesNotContain("form-channels", Features(withChannel: false));
        Assert.Contains("forms", Features(withChannel: false));
    }

    private static IReadOnlyCollection<string> Features(bool withChannel)
    {
        var services = new ServiceCollection();
        if (withChannel) services.AddScoped<IStructuredFormChannel, SilentChannel>();
        using var provider = services.BuildServiceProvider();
        return new FormClientCapabilitySource(new MemoryPrivateFileStore(), provider.GetRequiredService<IServiceProviderIsService>()).InstalledFeatures;
    }

    private static SaveFormDraftRequest Request(bool? appOnly) =>
        new(Guid.CreateVersion7(), Name, "rem", new FormDraftInput(1, "es", Submit, [], null, appOnly));

    private sealed class SilentChannel : IStructuredFormChannel
    {
        public string ChannelId => "silent";
        public Task<Result<StructuredFormPublicationResponse>> AcceptAsync(StructuredFormPublicationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<StructuredFormPublicationResponse>> GetPublicationAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<StructuredFormCheckResponse>> CheckAsync(StructuredFormCheckRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
