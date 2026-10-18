namespace AssistantQualification;

internal sealed record QualificationContinuationStep(string CaseId, QualificationLedgerEntry? Existing)
{
    public bool ShouldSubmit => Existing is null;

}

internal static class QualificationContinuationPolicy
{
    public static IReadOnlyList<QualificationContinuationStep> Build(IReadOnlyList<string> inventory,
        IReadOnlyList<QualificationLedgerEntry> ledger)
    {
        if(inventory.Count==0 || inventory.Distinct(StringComparer.Ordinal).Count()!=inventory.Count
            || ledger.Select(item=>item.CaseId).Distinct(StringComparer.Ordinal).Count()!=ledger.Count
            || ledger.Any(item=>!inventory.Contains(item.CaseId,StringComparer.Ordinal)
                || item.RequestId==Guid.Empty || item.ActorUserId==Guid.Empty || item.Phase is not ("before-submit" or "accepted")))
            throw new InvalidOperationException("Existing qualification inventory or ledger is inconsistent.");
        var entries=ledger.ToDictionary(item=>item.CaseId,StringComparer.Ordinal);
        return inventory.Select(id=>new QualificationContinuationStep(id,entries.GetValueOrDefault(id))).ToArray();
    }

    public static CaseEvidence ReevaluateHome(CaseEvidence original,bool factsMatch)
    {
        if(original.Checks.Count(check=>check.Id=="expected-fixture-facts")!=1)
            throw new InvalidOperationException("Existing Home evidence has no unique facts check.");
        return original with {Checks=original.Checks.Select(check=>check.Id=="expected-fixture-facts"
            ? new CheckEvidence(check.Id,factsMatch):check).ToArray(),
            Assessment="existing-acceptance-read-only; original-metrics-preserved; fixture-facts-reevaluated-in-memory"};
    }
    public static CaseEvidence ReevaluateChannelFieldSet(CaseEvidence original,bool exactFieldsMatch)
    {
        if(original.CaseId is not ("channel-original-language-es" or "channel-original-language-en")
            || original.Checks.Count(check=>check.Id=="exact-requested-field-set")!=1)
            throw new InvalidOperationException("Existing channel evidence is outside the finite optional-choice scope.");
        return original with {Checks=original.Checks.Select(check=>check.Id=="exact-requested-field-set"
            ? new CheckEvidence(check.Id,exactFieldsMatch):check).ToArray(),
            Assessment="existing-acceptance-read-only; original-metrics-preserved; finite-facts-backed-optional-choice-reevaluated"};
    }

    public static CaseEvidence ReevaluateChannelSuggestionMapping(CaseEvidence original,bool mappingMatches)
    {
        if(original.CaseId is not ("channel-suggestion-es" or "channel-suggestion-en")
            || original.Checks.Count(check=>check.Id=="preserves-exact-observed-mapping")!=1)
            throw new InvalidOperationException("Existing channel evidence is outside the finite suggestion-mapping scope.");
        return original with {Checks=original.Checks.Select(check=>check.Id=="preserves-exact-observed-mapping"
            ? new CheckEvidence(check.Id,mappingMatches):check).ToArray(),
            Assessment="existing-acceptance-read-only; original-metrics-preserved; finite-observed-source-notes-mapping-reevaluated"};
    }

}
