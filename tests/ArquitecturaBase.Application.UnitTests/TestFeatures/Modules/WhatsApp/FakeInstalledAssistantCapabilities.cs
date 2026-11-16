using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// The capabilities another installed module declares through the neutral port (B5): only their ids and versions, so the access policy
/// asks for them. It never runs one.
/// </summary>
internal sealed class FakeInstalledAssistantCapabilities(params AssistantCapabilityReference[] installed) : IAssistantCapabilitySource
{
    /// <summary>What Formularios declares that WhatsApp's contexts ask for: the conversation, the agent, Probar and building forms.</summary>
    public static readonly AssistantCapabilityReference[] Forms =
    [
        new("forms.assistant-candidates", 1), new("forms.propose-registration", 1), new("forms.clarify-registration", 1),
        new("forms.missing-request.propose", 1), new("forms.catalog-filters.metadata", 1), new("forms.catalog-filters.propose", 1),
        new("forms.authoring.forms", 1), new("forms.authoring.catalogs", 1), new("forms.authoring.propose-form", 2),
        new("forms.authoring.propose-catalog", 2), new("forms.authoring.propose-import", 1), new("forms.authoring.decline", 1),
    ];

    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } = installed.Select(reference => new AssistantCapabilityDefinition(
        reference.Id, reference.Version, AssistantCapabilityKind.Read, "Fictitious installed capability.", "{}", "{}", new([], []),
        new(1024, 1024, 1, 5))).ToArray();

    public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        throw new NotSupportedException();
}
