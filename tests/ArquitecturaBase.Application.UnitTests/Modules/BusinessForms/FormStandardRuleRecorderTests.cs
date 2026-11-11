using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// BI1-A: the rules a standard brings become the first revision of the copy's rules in the save that creates it, with the identities
// that save gave to its questions and options, and checked as the rule editor checks a save.
public sealed class FormStandardRuleRecorderTests
{
    private const string Amount = "0196f2c0-0000-7000-8000-000000000001", Priority = "0196f2c0-0000-7000-8000-000000000002",
        Copy = "0196f2c0-0000-7000-8000-000000000003", Channel = "0196f2c0-0000-7000-8000-000000000004", ByMail = "0196f2c0-0000-7000-8000-000000000005";
    private static readonly Guid AmountId = Guid.CreateVersion7(), PriorityId = Guid.CreateVersion7(), CopyId = Guid.CreateVersion7(),
        ChannelId = Guid.CreateVersion7(), ByMailId = Guid.CreateVersion7(), CatalogId = Guid.CreateVersion7(), UrgentId = Guid.CreateVersion7();

    private static readonly StructuredFormStandardRule[] Rules =
    [
        new("Received", [], [new("IssueDocument", IssuerId: "test.issuer"),
            new("NotifyAuthor", new Dictionary<string, string> { ["es"] = "Listo: {result:test.issuer.number}." }, IncludePdf: true),
            new("SendCopy", new Dictionary<string, string> { ["es"] = "Te mandamos {result:test.issuer.number}." })]),
        new("Received", [new(Priority, "in", ["code:urgent"]), new(Channel, "in", [ByMail]), new(Copy, "in", ["true"])],
            [new("NotifyAuthor", new Dictionary<string, string> { ["es"] = "Urgente." })]),
    ];

    [Fact]
    public void The_rules_name_the_questions_and_options_the_save_gave_and_keep_their_actions_in_order()
    {
        var definition = Definition();

        var ruleSet = FormStandardRuleRecorder.Create(definition, Rules, Identities(), Codes(), Issuers());

        Assert.Equal((definition.Id, 1L, "es", false), (ruleSet.DefinitionId, ruleSet.Revision, ruleSet.OriginalCulture, ruleSet.HasApproval));
        var rules = FormDraftPolicy.Read<FormRule[]>(ruleSet.RulesJson);
        Assert.Equal(["IssueDocument", "NotifyAuthor", "SendCopy"], rules[0].Actions.Select(action => action.Kind));
        Assert.Equal(("test.issuer", true, "Te mandamos {result:test.issuer.number}."),
            (rules[0].Actions[0].IssuerId, rules[0].Actions[1].IncludePdf, rules[0].Actions[2].Text!["es"]));
        Assert.Equal([(PriorityId, UrgentId.ToString("D")), (ChannelId, ByMailId.ToString("D")), (CopyId, "true")],
            rules[1].Conditions.Select(condition => (condition.FieldId, Assert.Single(condition.Values!))));
        Assert.Equal(6, rules.SelectMany(rule => rule.Actions).Select(action => action.Id).Append(rules[0].Id).Append(rules[1].Id).Distinct().Count());
        Assert.Equal(4, FormDraftPolicy.Read<FormVersionSchema>(ruleSet.QuestionSchemaJson).Questions.Count);
    }

    [Fact]
    public void Rules_that_the_editor_would_refuse_are_a_bug_of_the_module_that_brings_them()
    {
        Assert.Throws<InvalidOperationException>(() => FormStandardRuleRecorder.Create(Definition(), Rules, Identities(), Codes(), []));
        Assert.Throws<InvalidOperationException>(() => FormStandardRuleRecorder.Create(Definition(),
            [new("Received", [new("0196f2c0-0000-7000-8000-0000000000ff", "in", ["true"])], [new("NotifyAuthor", new Dictionary<string, string> { ["es"] = "Hola" })])],
            Identities(), Codes(), Issuers()));
    }

    [Fact]
    public async Task Adding_finds_the_option_codes_first_and_leaves_the_rules_as_the_active_revision_of_the_new_definition()
    {
        var repository = new RuleSets();
        var recorder = new FormStandardRuleRecorder(repository, new CodeReader(), [new Issuer()]);
        var catalogs = new Dictionary<string, string> { [Priority] = "test.priorities" };
        var ids = new Dictionary<string, Guid> { ["test.priorities"] = CatalogId };

        var missing = await recorder.PrepareAsync(Standard(Rules.Select(rule => rule with
        {
            Conditions = rule.Conditions.Select(condition => condition with { Values = condition.QuestionId == Priority ? ["code:nobody"] : condition.Values }).ToArray(),
        }).ToArray()), catalogs, ids, new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>(), CancellationToken.None);
        Assert.Equal(FormLibraryErrors.CatalogUnavailable.Code, missing.Error.Code);
        Assert.Null((await recorder.PrepareAsync(Standard(null), catalogs, ids, new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>(), CancellationToken.None)).Value);

        var prepared = await recorder.PrepareAsync(Standard(Rules), catalogs, ids, new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>(), CancellationToken.None);
        var definition = Definition();
        prepared.Value!(definition, Identities());

        var ruleSet = Assert.Single(repository.Added);
        Assert.Equal(ruleSet.Id, definition.ActiveRuleSetId);
        Assert.Equal(UrgentId, FormDraftPolicy.Read<FormRule[]>(ruleSet.RulesJson)[1].Conditions[0].Values!.Select(Guid.Parse).Single());
    }

    private static StructuredFormStandard Standard(IReadOnlyList<StructuredFormStandardRule>? rules) => new("test.issuer.standard", 1,
        new Dictionary<string, string> { ["es"] = "Prueba" }, new Dictionary<string, string> { ["es"] = "Prueba" }, "PRB", "{}",
        new Dictionary<string, string>(), Rules: rules);

    private static BusinessFormDefinition Definition()
    {
        var definition = BusinessFormDefinition.Create();
        CanonicalFormField Field(Guid id, string key, string type) => new(id, key, type, new Dictionary<string, string> { ["es"] = key }, null, false, null, null);
        var schema = new FormQuestionSchema(1,
        [
            Field(AmountId, "amount", "decimal"), Field(PriorityId, "priority", "choice") with { Source = new FormQuestionSource(CatalogId) },
            Field(CopyId, "copy", "boolean"),
            Field(ChannelId, "channel", "choice") with { Options = [new CanonicalFormChoice(ByMailId, new Dictionary<string, string> { ["es"] = "Por correo" })] },
        ]);
        Assert.True(definition.SetDraft(1, "Prueba", """{"es":"Prueba"}""", "PRB", "es", """{"es":"Enviar"}""", FormDraftPolicy.Serialize(schema), 4, true).IsSuccess);
        return definition;
    }

    private static FormIdentityMap Identities() => new(
        [new(Amount, AmountId, "amount", null), new(Priority, PriorityId, "priority", null), new(Copy, CopyId, "copy", null), new(Channel, ChannelId, "channel", null)],
        [new(Channel, ByMail, ChannelId, ByMailId)]);

    private static Dictionary<string, IReadOnlyDictionary<string, Guid>> Codes() => new() { [Priority] = new Dictionary<string, Guid> { ["urgent"] = UrgentId } };

    private static FormRuleIssuerResponse[] Issuers() => [new("test.issuer", new Dictionary<string, string> { ["es"] = "Emitir" },
        [new StructuredRecordResultField("number", new Dictionary<string, string> { ["es"] = "Número" }, StructuredRecordResultField.Text)])];

    private sealed class RuleSets : IFormRuleRepository
    {
        public List<BusinessFormRuleSet> Added { get; } = [];
        public Task<BusinessFormRuleSet?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Added.SingleOrDefault(item => item.Id == id));
        public void Add(BusinessFormRuleSet ruleSet) => Added.Add(ruleSet);
    }

    private sealed class CodeReader : IFormCatalogItemCodeReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, Guid>> ListActiveAsync(Guid catalogId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, Guid>>(catalogId == CatalogId && codes.Contains("urgent") ? new Dictionary<string, Guid> { ["urgent"] = UrgentId } : new Dictionary<string, Guid>());
    }

    private sealed class Issuer : IStructuredRecordIssuer
    {
        public string IssuerId => "test.issuer";
        public IReadOnlyDictionary<string, string> Name { get; } = new Dictionary<string, string> { ["es"] = "Emitir" };
        public string StandardKey => "test.issuer.standard";
        public string ReviewPermission => "test.issue";
        public IReadOnlyList<StructuredRecordResultField> ResultFields => Issuers()[0].ResultFields;
        public string? CopyRecipient(IReadOnlyList<StructuredIssueDatum> data) => null;
        public Task<StructuredIssuePreview> PreviewAsync(IReadOnlyList<StructuredIssueDatum> data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> EnqueueAsync(StructuredIssueRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> GetAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> ReviewAsync(Guid runId, StructuredIssueReview review, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> CancelAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StructuredIssueResponse> EnqueueReversalAsync(Guid reversalRunId, Guid issuedRunId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
