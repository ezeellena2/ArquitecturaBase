using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The latest documents read of a turn that a proposal may take its forms from (decision 8): the candidates read of the Subject, in
/// its culture, or the form read of the reviewer with only the form the server fixed. None when the turn read nothing (a document
/// nobody may load is still «unknown»); a read of another person, culture or form fails.
/// </summary>
internal static class FormDocumentObservationPolicy
{
    public static Result<FormObservedDocumentSelection?> Find(IReadOnlyList<AssistantObservedRead> reads, Guid turnId, AssistantChannelContext context)
    {
        var matching = reads.Where(read => read.TurnId == turnId && read.Capability.Id is BusinessFormDocumentAssistantCapabilitySource.CandidatesId
            or BusinessFormDocumentAssistantCapabilitySource.FormId).ToArray();
        if (matching.Length == 0) return Result.Success<FormObservedDocumentSelection?>(null);
        if (matching.Select(read => read.Ordinal).Distinct().Count() != matching.Length) return FormRecordErrors.InvalidResponse;
        var latest = matching.MaxBy(read => read.Ordinal)!;
        if (latest.Ordinal < 1 || latest.Capability.Version != BusinessFormDocumentAssistantCapabilitySource.Version
            || latest.Payload is not FormDocumentReadPayload payload || !FormDocumentCapabilityCodec.ValidRead(payload) || payload.Culture != context.Culture)
            return FormRecordErrors.InvalidResponse;
        var owner = latest.Capability.Id == BusinessFormDocumentAssistantCapabilitySource.CandidatesId
            ? payload.UserId == context.SubjectUserId
            : payload.UserId == context.ActorUserId && context.SubjectUserId is null && context.FocusDefinitionId is { } focus
                && payload.Forms.All(form => form.DefinitionId == focus);
        return owner ? new FormObservedDocumentSelection(latest.Capability.Id, new(latest.Ordinal, payload)) : FormRecordErrors.InvalidResponse;
    }

    /// <summary>The same check on a proposal the engine already returned: its read is the latest of its turn and its candidates are in it.</summary>
    public static bool Authentic(AssistantObservedProposal proposal, out FormDocumentEntryProposal payload, out FormObservedDocumentSelection? read)
    {
        payload = null!;
        read = null;
        if (proposal.Id == Guid.Empty || proposal.TurnId == Guid.Empty
            || proposal.Capability is not { Id: BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, Version: BusinessFormDocumentAssistantCapabilitySource.Version }
            || proposal.Payload is not FormDocumentEntryProposal entry || !FormDocumentCapabilityCodec.ValidProposal(entry)) return false;
        var latest = proposal.ObservedReads.Where(observed => observed.TurnId == proposal.TurnId && observed.Capability.Id is BusinessFormDocumentAssistantCapabilitySource.CandidatesId
            or BusinessFormDocumentAssistantCapabilitySource.FormId).MaxBy(observed => observed.Ordinal);
        if (entry.Candidates.Count == 0) { payload = entry; return true; }
        if (latest is null || latest.Ordinal != entry.ReadOrdinal || latest.Payload is not FormDocumentReadPayload observedPayload
            || !FormDocumentCapabilityCodec.ValidRead(observedPayload)
            || entry.Candidates.Any(candidate => !observedPayload.Forms.Any(form => form.DefinitionId == candidate.DefinitionId && form.VersionId == candidate.VersionId)))
            return false;
        payload = entry;
        read = new(latest.Capability.Id, new(latest.Ordinal, observedPayload));
        return true;
    }
}

/// <summary>The latest documents read of a turn and which of the two reads it was.</summary>
internal sealed record FormObservedDocumentSelection(string Capability, FormObservedDocumentRead Read)
{
    public override string ToString() => "FormObservedDocumentSelection { Content = [REDACTED] }";
}
