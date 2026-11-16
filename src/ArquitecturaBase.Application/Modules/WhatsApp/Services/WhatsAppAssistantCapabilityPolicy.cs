using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// The capabilities a WhatsApp context asks for: the agent's when one is saved, and what it asks of Formularios (the `forms.*`
/// capabilities) only while an installed owner declares it through the neutral port (B5). The engine rejects a whole context that names a
/// capability nobody declares, so without that module the conversation keeps its own capabilities and still gets answers. WhatsApp's own
/// capabilities are never filtered: a mistake there still fails.
/// </summary>
internal sealed class WhatsAppAssistantCapabilityPolicy(IWhatsAppAssistantAgentReader agents, IEnumerable<IAssistantCapabilitySource> sources)
{
    private const string FormsPrefix = "forms.";
    private HashSet<AssistantCapabilityReference>? declared;

    /// <summary>A conversation's context: with the agent's capabilities when one is saved, and only what is installed.</summary>
    public async Task<AssistantChannelContext> ForConversationAsync(AssistantChannelContext context, bool automatic, CancellationToken ct)
    {
        if (await agents.FindMainAsync(ct) is not null) context = WhatsAppAssistantAccessPolicy.WithAgent(context, automatic);
        return Installed(context);
    }

    public AssistantChannelContext Installed(AssistantChannelContext context) => context with
    {
        AllowedCapabilities = context.AllowedCapabilities.Where(IsInstalled).ToArray(),
        PreloadedCapabilities = context.PreloadedCapabilities?.Where(IsInstalled).ToArray(),
    };

    private bool IsInstalled(AssistantCapabilityReference reference) =>
        !reference.Id.StartsWith(FormsPrefix, StringComparison.Ordinal)
        || (declared ??= sources.SelectMany(source => source.Definitions)
            .Select(definition => new AssistantCapabilityReference(definition.Id, definition.Version)).ToHashSet()).Contains(reference);
}
