using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace AssistantQualification;

internal sealed record AuthoringQuestionExpectation(string Label, string Type, bool Required, string[]? Options = null, string? Catalog = null);
internal sealed record AuthoringFieldExpectation(string Name, string Type);
// What an authoring case expects (IA1-A in Home, IA3-A in the WhatsApp automatic channel). A null Capability expects no
// proposal at all: Home without the permission, or a request too unclear to build.
internal sealed record AuthoringExpectation(string? Capability, string? Name = null, string? Prefix = null,
    AuthoringQuestionExpectation[]? Questions = null, string? ValueType = null, string? Code = null, string? Option = null,
    AuthoringFieldExpectation[]? Fields = null, string? DeclineReason = null, string? ExistingForm = null, string? ExistingCatalog = null,
    string? Continues = null);

// Finite in-memory oracles over public fixture facts; nothing here is exported except fixed check IDs and booleans.
// Texts compare without case, accents or punctuation; types, requirement, order, identities and value types compare exactly.
internal static partial class AuthoringOracle
{
    public const string ProposeForm = "forms.authoring.propose-form";
    public const string ProposeCatalog = "forms.authoring.propose-catalog";
    public const string Decline = "forms.authoring.decline";
    public const string PeopleCatalog = "forms.catalog-filters.propose";
    private static readonly Dictionary<string, Type> Reads = new(StringComparer.Ordinal)
    {
        ["forms.authoring.forms"] = typeof(FormAuthoringFormsPayload),
        ["forms.authoring.catalogs"] = typeof(FormAuthoringCatalogsPayload),
        ["forms.catalog-filters.metadata"] = typeof(FormCatalogMetadataPayload),
    };

    public static IReadOnlyList<CheckEvidence> PayloadChecks(AuthoringExpectation expected, string culture, AssistantCapabilityPayload? payload,
        QualificationChannelFixture fixture, QualificationFixture actors, AssistantCapabilityPayload? previous) => expected.Capability switch
    {
        ProposeForm => FormChecks(expected, culture, payload as FormAuthoringFormProposal, fixture, previous),
        ProposeCatalog => CatalogChecks(expected, culture, payload as FormAuthoringCatalogProposal, fixture),
        Decline => DeclineChecks(expected, payload as FormAuthoringDecline, fixture),
        PeopleCatalog => [new("authoring-payload-type", payload is FormCatalogProposalPayload),
            ..ChannelRunner.CatalogFilterChecks(payload as FormCatalogProposalPayload, actors, false)],
        null => [],
        _ => throw new InvalidOperationException("Unknown authoring expectation."),
    };

    private static List<CheckEvidence> FormChecks(AuthoringExpectation expected, string culture, FormAuthoringFormProposal? proposal,
        QualificationChannelFixture fixture, AssistantCapabilityPayload? previous)
    {
        var checks = new List<CheckEvidence>
        {
            new("authoring-payload-type", proposal is not null),
            new("authoring-culture", proposal?.Culture == culture),
            new("authoring-requested-name", proposal is not null && (expected.Name is null ? Canonical(proposal.Name).Length > 0 : SameText(expected.Name, proposal.Name))),
            new("authoring-name-not-in-use", proposal is not null && !fixture.Forms.SelectMany(form => form.Names.Values).Any(name => SameText(name, proposal.Name))),
            new("authoring-prefix-valid-and-free", proposal is not null && PrefixPattern().IsMatch(proposal.Prefix)
                && fixture.Forms.All(form => !string.Equals(form.Prefix, proposal.Prefix, StringComparison.Ordinal))
                && (expected.Prefix is null || string.Equals(expected.Prefix, proposal.Prefix, StringComparison.Ordinal))),
            new("authoring-exact-questions", proposal is not null && ExactQuestions(expected.Questions ?? [], proposal.Questions, fixture)),
        };
        // An adjustment proposes the whole form again: the same name and prefix the previous proposal of the conversation had.
        // The answer to «what should I build?» proposes for the first time, right after that question («pregunta y espera»).
        if (expected.Continues is not null)
            checks.Add(previous is FormAuthoringDecline asked
                ? new("authoring-answers-the-question", proposal is not null && asked is { Reason: "unclear" })
                : new("authoring-keeps-previous-identity", proposal is not null && previous is FormAuthoringFormProposal shown
                    && SameText(shown.Name, proposal.Name) && string.Equals(shown.Prefix, proposal.Prefix, StringComparison.Ordinal)));
        return checks;
    }

    private static bool ExactQuestions(AuthoringQuestionExpectation[] expected, IReadOnlyList<FormAuthoringProposedQuestion> actual,
        QualificationChannelFixture fixture)
    {
        if (expected.Length == 0 || expected.Length != actual.Count) return false;
        for (var index = 0; index < expected.Length; index++)
        {
            var (wanted, proposed) = (expected[index], actual[index]);
            var options = wanted.Options ?? [];
            if (!SameText(wanted.Label, proposed.Label) || !string.Equals(wanted.Type, proposed.Type, StringComparison.Ordinal)
                || wanted.Required != proposed.Required || options.Length != proposed.Options.Count
                || options.Where((option, position) => !SameText(option, proposed.Options[position])).Any()) return false;
            var catalogs = wanted.Catalog is null ? [] : fixture.Catalogs.Where(catalog => catalog.Alias == wanted.Catalog).ToArray();
            if (wanted.Type == "catalog" ? catalogs.Length != 1 || proposed.Catalog?.Id != catalogs[0].Id : proposed.Catalog is not null) return false;
        }
        return true;
    }

    private static IReadOnlyList<CheckEvidence> CatalogChecks(AuthoringExpectation expected, string culture, FormAuthoringCatalogProposal? proposal,
        QualificationChannelFixture fixture) =>
    [
        new("authoring-payload-type", proposal is not null),
        new("authoring-culture", proposal?.Culture == culture),
        new("authoring-requested-name", proposal is not null && (expected.Name is null ? Canonical(proposal.Name).Length > 0 : SameText(expected.Name, proposal.Name))),
        new("authoring-name-not-in-use", proposal is not null && !fixture.Catalogs.SelectMany(catalog => catalog.Names.Values).Any(name => SameText(name, proposal.Name))),
        new("authoring-value-type", proposal is not null && string.Equals(expected.ValueType, proposal.ValueType, StringComparison.Ordinal)),
        // Code and option describe what each option carries: they must name the requested concept, and not each other's.
        new("authoring-code-and-option", proposal is not null && expected is { Code: { } code, Option: { } option }
            && ContainsPhrase(proposal.Code, code) && ContainsPhrase(proposal.Option, option)
            && !ContainsPhrase(proposal.Code, option) && !ContainsPhrase(proposal.Option, code)),
        new("authoring-exact-fields", proposal is not null && expected.Fields is { Length: > 0 } fields && fields.Length == proposal.Fields.Count
            && fields.Select((field, index) => SameText(field.Name, proposal.Fields[index].Name)
                && string.Equals(field.Type, proposal.Fields[index].Type, StringComparison.Ordinal)).All(same => same)),
    ];

    // BusinessForms checked the reason; the oracle checks it is the requested one and names exactly the existing form or catalog.
    private static IReadOnlyList<CheckEvidence> DeclineChecks(AuthoringExpectation expected, FormAuthoringDecline? decline, QualificationChannelFixture fixture)
    {
        var form = expected.ExistingForm is null ? null : fixture.Forms.SingleOrDefault(item => item.Alias == expected.ExistingForm);
        var catalog = expected.ExistingCatalog is null ? null : fixture.Catalogs.SingleOrDefault(item => item.Alias == expected.ExistingCatalog);
        return
        [
            new("authoring-payload-type", decline is not null),
            new("authoring-decline-reason", decline is not null && string.Equals(expected.DeclineReason, decline.Reason, StringComparison.Ordinal)),
            new("authoring-decline-target", decline is not null && expected.DeclineReason switch
            {
                "noPermission" or "unclear" => decline is { DefinitionId: null, CatalogId: null, Name.Length: 0 },
                "formExists" => form is not null && decline.DefinitionId == form.DefinitionId && decline.CatalogId is null
                    && form.Names.Values.Any(name => SameText(name, decline.Name)),
                "catalogExists" => catalog is not null && decline.CatalogId == catalog.Id && decline.DefinitionId is null
                    && catalog.Names.Values.Any(name => SameText(name, decline.Name)),
                _ => false,
            }),
        ];
    }

    // The proposal must rest on reads confirmed in an earlier step of the same turn (the tool descriptions ask to read first).
    public static bool ReadsBeforeProposal(AssistantObservedProposal? proposal, IEnumerable<string> requiredTools) =>
        proposal is not null && requiredTools.Where(Reads.ContainsKey).All(tool => proposal.ObservedReads.Any(read =>
            read.TurnId == proposal.TurnId && read.Ordinal > 0 && read.Capability.Id == tool && read.Capability.Version == 1
            && read.Payload.GetType() == Reads[tool]));

    // A rejected proposal never gets an Id; after the last rejection the model must have proposed the expected capability again.
    public static bool RejectionsRecovered(IReadOnlyList<ObservedCapability> tools, string capability)
    {
        var last = -1;
        for (var index = 0; index < tools.Count; index++) if (tools[index].State == "rejected") last = index;
        return last < 0 || tools.Skip(last + 1).Any(tool => tool.Id == capability && tool.Version == 1 && tool.State == "proposed");
    }

    // One proposal per turn: every proposal call (accepted or rejected) is of the expected capability; null expects none at all.
    public static bool OnlyProposalsOf(IReadOnlyList<ObservedCapability> tools, IReadOnlySet<string> proposalCapabilities, string? capability)
    {
        var proposals = tools.Where(tool => proposalCapabilities.Contains(tool.Id)).ToArray();
        return capability is null ? proposals.Length == 0
            : proposals.Length > 0 && proposals.All(tool => tool.Id == capability) && proposals.Any(tool => tool.State == "proposed");
    }

    // What the sealed context offered after the engine filtered the catalog by the account's current permissions.
    public static bool Offered(IReadOnlyCollection<string> offered, IEnumerable<string> required, IEnumerable<string> forbidden) =>
        required.All(id => offered.Contains(id, StringComparer.Ordinal)) && !forbidden.Any(id => offered.Contains(id, StringComparer.Ordinal));

    public static bool SameText(string? expected, string? actual) => Canonical(expected) is { Length: > 0 } canonical && canonical == Canonical(actual);

    public static bool ContainsPhrase(string? actual, string phrase)
    {
        var words = Canonical(actual).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wanted = Canonical(phrase).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wanted.Length == 0 || wanted.Length > words.Length) return false;
        for (var start = 0; start + wanted.Length <= words.Length; start++)
            if (words.Skip(start).Take(wanted.Length).SequenceEqual(wanted, StringComparer.Ordinal)) return true;
        return false;
    }

    public static string Canonical(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // The authoring validator's prefix rule (SaveFormDraftRequestValidator): the proposal is normalized to uppercase.
    [GeneratedRegex("^[A-Z][A-Z0-9]{0,11}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}
