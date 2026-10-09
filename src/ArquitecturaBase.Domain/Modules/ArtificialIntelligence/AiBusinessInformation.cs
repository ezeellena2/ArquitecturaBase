using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public sealed class AiBusinessInformation : Entity, IAuditable
{
    private AiBusinessInformation() { }
    public int Scope { get; private set; } = 1;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Hours { get; private set; } = string.Empty;
    public string Location { get; private set; } = string.Empty;
    public string Services { get; private set; } = string.Empty;
    public string Policies { get; private set; } = string.Empty;
    public string Faq { get; private set; } = string.Empty;
    public AiBusinessDetails? Details { get; private set; }
    public long Revision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static AiBusinessInformation Create() => new();
    public Result Update(string name, string description, string hours, string location, string services, string policies, string faq, AiBusinessDetails? details = null)
    {
        var normalized = details?.Normalize();
        if (normalized is { IsFailure: true }) return normalized.Error;
        var nextDetails = normalized?.Value ?? Details;
        string[] values = [name?.Trim() ?? "", description?.Trim() ?? "", hours?.Trim() ?? "",
            location?.Trim() ?? "", services?.Trim() ?? "", policies?.Trim() ?? "", faq?.Trim() ?? ""];
        int[] limits = [120, 1000, 500, 500, 2000, 2000, 4000];
        if (values[0].Length == 0 || (values.Skip(1).All(string.IsNullOrEmpty) && nextDetails?.HasConfirmedFacts() != true)
            || values.Where((value, index) => value.Length > limits[index]).Any())
            return AiBusinessInformationErrors.Invalid;
        Name = values[0]; Description = values[1]; Hours = values[2]; Location = values[3];
        Services = values[4]; Policies = values[5]; Faq = values[6];
        Details = nextDetails;
        Revision = checked(Revision + 1);
        return Result.Success();
    }
    public override string ToString() => "AiBusinessInformation { content: [REDACTED] }";
}
