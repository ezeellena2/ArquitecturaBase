using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Assistant;

public static class AssistantOperationErrors
{
    public static readonly Error Unavailable = Error.Conflict("Assistant.Operation.Unavailable", "The assistant operation channel is unavailable.");
}
