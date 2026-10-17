namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal static class WhatsAppAssistantReplyCodec
{
    private const string Prefix = "FORM_ASSISTANT:";
    public static string Encode(Guid requestId, Guid definitionId, Guid versionId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(requestId,Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(definitionId,Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(versionId,Guid.Empty);
        return $"{Prefix}{requestId:N}:{definitionId:N}:{versionId:N}";
    }
    public static bool TryParse(string? value, out Guid requestId, out Guid definitionId, out Guid versionId)
    {
        requestId=Guid.Empty;definitionId=Guid.Empty;versionId=Guid.Empty;
        if(value is null || value.Length!=Prefix.Length+98 || !value.StartsWith(Prefix,StringComparison.Ordinal))return false;
        var content=value.AsSpan(Prefix.Length);
        if(content[32]!=':' || content[65]!=':' || !Guid.TryParseExact(content[..32],"N",out var request)
            || !Guid.TryParseExact(content.Slice(33,32),"N",out var definition) || !Guid.TryParseExact(content[66..],"N",out var version)
            || request==Guid.Empty || definition==Guid.Empty || version==Guid.Empty)return false;
        requestId=request;definitionId=definition;versionId=version;return true;
    }
}
