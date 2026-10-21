using ArquitecturaBase.Application.Interfaces.Channels;

namespace ArquitecturaBase.Application.Channels;

internal sealed class DisabledAssistantOperationCompletionSignal : IAssistantOperationCompletionSignal
{
    public void Notify(string channelId) { }
}
