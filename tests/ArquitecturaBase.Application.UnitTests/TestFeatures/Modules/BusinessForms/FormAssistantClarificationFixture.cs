using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormAssistantClarificationFixture
{
    public FormAssistantClarificationFixture(int count = 2)
    {
        for (var index = 1; index < count; index++)
            Inner.Rows.Add(Inner.Rows[0] with { DefinitionId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(),
                NameJson = FormDraftPolicy.Serialize(new Dictionary<string, string> { ["es"] = "Entrega " + index, ["en"] = "Delivery " + index }) });
    }
    public FormAssistantFixture Inner { get; } = new();
    public string Culture { get; set; } = "es";
    public FormAssistantClarificationPolicy Policy => new(Inner.Candidates, Inner);
    public AssistantChannelContext Context => Inner.Context with { Culture = Culture,
        AllowedCapabilities = [.. Inner.Context.AllowedCapabilities, new(BusinessFormAssistantCapabilitySource.ClarificationId, 1)] };
    public async Task UseEnglishAsync(CancellationToken ct)
    {
        Culture = "en";
        await Inner.Users.UpdateProfileAsync(Inner.UserId, null, Culture, "America/Argentina/Buenos_Aires", ct);
    }
    public FormAssistantCandidatesPayload Snapshot()
    {
        var template = Inner.ReadPayload().Forms[0];
        return new("complete", Inner.UserId, Culture, true, Inner.Rows.Select(row => template with
        { DefinitionId = row.DefinitionId, VersionId = row.VersionId, DefinitionRevision = row.DefinitionRevision,
          Name = FormDraftPolicy.Read<Dictionary<string, string>>(row.NameJson)[Culture], OfferWhen = row.OfferWhen }).ToArray(), []);
    }
    public AssistantObservedRead Read(Guid turnId) => new(turnId, 1, new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), Snapshot());
    public string Arguments() => FormDraftPolicy.Serialize(new { forms = Inner.Rows.Select(row => new { row.DefinitionId, row.VersionId }).ToArray() });
    public AssistantCapabilityRequest Request()
    {
        var turn = Guid.CreateVersion7();
        return new(turn, Context, new(BusinessFormAssistantCapabilitySource.ClarificationId, 1), Arguments(), [Read(turn)]);
    }
    public FormAssistantClarificationPayload Payload() => new("clarification", 1,
        Inner.Rows.Select(row => new FormAssistantClarificationReference(row.DefinitionId, row.VersionId, row.DefinitionRevision)).ToArray());
    public AssistantObservedProposal Proposal()
    {
        var turn = Guid.CreateVersion7();
        return new(turn, Guid.CreateVersion7(), new(BusinessFormAssistantCapabilitySource.ClarificationId, 1), Payload()) { ObservedReads = [Read(turn)] };
    }
}
