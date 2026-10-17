using ArquitecturaBase.Application.Interfaces.Integrations.Request;

namespace AssistantQualification;

// Technical request context of the console only; worker identities still come from the durable claim.
internal sealed class QualificationCurrentUser : ICurrentUser
{
    public Guid? UserId { get; private set; }
    public bool IsAuthenticated => UserId is not null;
    public void Set(Guid actorUserId) => UserId = actorUserId;
}
