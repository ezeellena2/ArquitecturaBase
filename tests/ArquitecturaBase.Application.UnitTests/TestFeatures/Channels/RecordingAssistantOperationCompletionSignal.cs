using ArquitecturaBase.Application.Interfaces.Channels;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;

internal sealed class RecordingAssistantOperationCompletionSignal : IAssistantOperationCompletionSignal
{
    public List<string> Channels { get; } = [];
    public Action? BeforeNotify { get; set; }
    public void Notify(string channelId)
    {
        BeforeNotify?.Invoke();
        Channels.Add(channelId);
    }
}
