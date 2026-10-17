using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public enum BusinessFormMissingTopicState { Pending, Discarded }
public sealed class BusinessFormMissingTopic : Entity, IAuditable
{
    private BusinessFormMissingTopic() { }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string OriginalCulture { get; private set; } = "es";
    public long Revision { get; private set; } = 1;
    public BusinessFormMissingTopicState State { get; private set; }
    public Guid? DiscardedByUserId { get; private set; }
    public DateTime? DiscardedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static Result<BusinessFormMissingTopic> Create(string name, string description, string originalCulture)
    {
        if (!Text(name, 120) || !Text(description, 500) || originalCulture is not ("es" or "en"))
            return FormMissingRequestErrors.ContentInvalid;
        return new BusinessFormMissingTopic { Name = name.Trim(), NormalizedName = NormalizeName(name),
            Description = description.Trim(), OriginalCulture = originalCulture };
    }
    public Result RegisterRequest()
    {
        if (Revision >= 9007199254740991) return FormMissingRequestErrors.RevisionConflict;
        State = BusinessFormMissingTopicState.Pending; Revision++;
        return Result.Success();
    }
    public Result Discard(long expectedRevision, Guid actorUserId, DateTime nowUtc)
    {
        if (Revision != expectedRevision || Revision >= 9007199254740991 || State != BusinessFormMissingTopicState.Pending)
            return FormMissingRequestErrors.RevisionConflict;
        if (actorUserId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc) return FormMissingRequestErrors.ContentInvalid;
        State = BusinessFormMissingTopicState.Discarded; DiscardedByUserId = actorUserId; DiscardedAtUtc = nowUtc; Revision++;
        return Result.Success();
    }
    public static string NormalizeName(string name) => string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static bool Text(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && StorableText.IsStorable(value);
    public override string ToString() => "BusinessFormMissingTopic { Content = [REDACTED] }";
}
