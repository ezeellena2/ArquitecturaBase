using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public sealed class BusinessFormMissingRequest : Entity
{
    private BusinessFormMissingRequest() { }
    public Guid SourceReceiptId { get; private set; }
    public Guid TopicId { get; private set; }
    public Guid SubjectUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public static Result<BusinessFormMissingRequest> Create(Guid sourceReceiptId, Guid topicId, Guid subjectUserId, DateTime requestedAtUtc)
    {
        if (sourceReceiptId == Guid.Empty || topicId == Guid.Empty || subjectUserId == Guid.Empty || requestedAtUtc.Kind != DateTimeKind.Utc)
            return FormMissingRequestErrors.ContentInvalid;
        return new BusinessFormMissingRequest { SourceReceiptId = sourceReceiptId, TopicId = topicId, SubjectUserId = subjectUserId,
            RequestedAtUtc = requestedAtUtc };
    }
    public override string ToString() => "BusinessFormMissingRequest { Identity = [REDACTED] }";
}
