using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed partial class WhatsAppFlowClient
{
    public Task<WhatsAppFlowEffectResponse> DeprecateAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken) =>
        EffectAsync(account, Uri.EscapeDataString(metaFlowId) + "/deprecate", new StringContent(""), false, cancellationToken,
            requireAcknowledgement: true);

    public async Task<WhatsAppFlowStateResponse> GetStateAsync(WhatsAppFlowAccountContext account, string metaFlowId,
        string technicalName, CancellationToken cancellationToken)
    {
        if (!Matches(account)) return new(false, WhatsAppFlowRemoteState.Unknown);
        const string fields = "id,name,status,whatsapp_business_account";
        using var document = await ReadGraphAsync(options.Value.GraphApiVersion + "/" + Uri.EscapeDataString(metaFlowId)
            + "?fields=" + Uri.EscapeDataString(fields), cancellationToken);
        if (document is null) return new(false, WhatsAppFlowRemoteState.Unknown);
        var root = document.RootElement;
        var matches = Text(root, "id") == metaFlowId && Text(root, "name") == technicalName
            && root.TryGetProperty("whatsapp_business_account", out var owner) && Text(owner, "id") == account.WabaId;
        var state = !matches ? WhatsAppFlowRemoteState.Unknown : Text(root, "status") switch
        {
            "PUBLISHED" => WhatsAppFlowRemoteState.Published,
            "DEPRECATED" => WhatsAppFlowRemoteState.Deprecated,
            _ => WhatsAppFlowRemoteState.Unknown
        };
        return new(matches, state);
    }
}
