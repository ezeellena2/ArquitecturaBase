using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormActionRunRecorder(
    IFormRuleRepository rules, IFormActionRunRepository runs, IFormActionRecipientReader recipients,
    IFormPdfActionService pdf, IFormPdfDesignReader pdfDesigns, TimeProvider timeProvider)
{
    public async Task RecordAsync(
        BusinessFormRecord record, FormActionRecordSnapshot snapshot, string eventName, CancellationToken cancellationToken)
    {
        if (record.RuleSetId is not { } ruleSetId)
            return;
        var ruleSet = await rules.GetAsync(ruleSetId, cancellationToken)
            ?? throw new InvalidOperationException("The captured rule revision is missing.");
        var revisionRules = FormDraftPolicy.Read<FormRule[]>(ruleSet.RulesJson);
        var eventRules = revisionRules.Where(rule => rule.Event == eventName).ToArray();
        if (eventRules.Length == 0)
            return;
        var completeCultures = await CompleteCulturesAsync(record, snapshot, revisionRules, cancellationToken);
        var existing = (await runs.ListForEventAsync(record.Id, eventName, cancellationToken))
            .ToDictionary(run => (run.RuleId, run.ActionId));
        var questions = FormDraftPolicy.Read<FormVersionSchema>(ruleSet.QuestionSchemaJson);

        foreach (var rule in eventRules)
        {
            var applies = FormRecordConditionEvaluator.Matches(rule.Conditions, snapshot);
            Guid? dependency = null;
            Guid? pdfRunId = null;
            IReadOnlyList<string>? pdfCultures = null;
            foreach (var (action, index) in rule.Actions.Select((action, index) => (action, index)))
            {
                if (existing.TryGetValue((rule.Id, action.Id), out var prior))
                {
                    dependency = prior.Id;
                    if (action.Kind == "GeneratePdf")
                    {
                        pdfRunId = prior.Id;
                        pdfCultures = FormDraftPolicy.Read<FormActionSnapshot>(prior.SnapshotJson).Pdf?.Cultures;
                    }
                    continue;
                }

                var run = BusinessFormActionRun.Create(
                    record.DefinitionId, record.Id, ruleSetId, rule.Id, action.Id, eventName, action.Kind, index,
                    dependency, "", applies, timeProvider.GetUtcNow().UtcDateTime);
                FormPdfActionSnapshotResponse? pdfSnapshot = null;
                string? failure = null;
                var targets = new List<FormActionRecipient>();
                if (applies && action.Kind == "GeneratePdf")
                {
                    var prepared = await pdf.PrepareAsync(run.Id, snapshot, cancellationToken);
                    if (prepared.IsSuccess)
                    {
                        pdfSnapshot = prepared.Value with
                        {
                            Cultures = prepared.Value.Cultures.Intersect(completeCultures).ToArray()
                        };
                        pdfCultures = pdfSnapshot.Cultures;
                    }
                    else
                        failure = prepared.Error.Code;
                    pdfRunId = run.Id;
                }

                if (applies && action.Kind is "NotifyAuthor" or "NotifyRole")
                {
                    IReadOnlyList<FormActionRecipientRow> people;
                    if (action.Kind == "NotifyRole")
                        people = await recipients.ListForRoleAsync(action.RoleId!.Value, cancellationToken);
                    else
                        people = [await recipients.FindAsync(record.ActorUserId, cancellationToken)
                            ?? new(record.ActorUserId, snapshot.OriginalCulture)];
                    if (people.Count == 0)
                        failure = FormActionErrors.RecipientUnavailable.Code;
                    foreach (var person in people)
                    {
                        var culture = FormActionMessagePolicy.Culture(
                            person.Culture, snapshot.OriginalCulture, completeCultures, action.IncludePdf ? pdfCultures : null);
                        var body = FormActionMessagePolicy.Render(action, questions, snapshot, culture);
                        if (body.Length > (action.IncludePdf ? 1024 : 4096))
                            failure = FormActionErrors.MessageTooLong.Code;
                        else
                            targets.Add(new(person.UserId, culture, body));
                    }
                }

                run.SealSnapshot(FormDraftPolicy.Serialize(new FormActionSnapshot(
                    snapshot, action, questions, pdfSnapshot, action.IncludePdf ? pdfRunId : null, targets, failure)));
                runs.Add(run);
                dependency = run.Id;
            }
        }
    }

    private async Task<IReadOnlyList<string>> CompleteCulturesAsync(
        BusinessFormRecord record, FormActionRecordSnapshot snapshot, IReadOnlyList<FormRule> revisionRules,
        CancellationToken cancellationToken)
    {
        var complete = FormActionMessagePolicy.CompleteCultures(revisionRules, snapshot.OriginalCulture);
        if (!revisionRules.SelectMany(rule => rule.Actions).Any(action => action.Kind == "GeneratePdf"))
            return complete;

        var current = await pdfDesigns.FindCurrentAsync(record.DefinitionId, cancellationToken);
        var captured = snapshot.Response.Record;
        var schema = FormDraftPolicy.Read<FormVersionSchema>(captured.SchemaJson);
        var design = FormPdfDesignPolicy.Resolve(current, captured, schema);
        var pdfCultures = FormPdfDesignPolicy.CompleteCultures(design, snapshot.OriginalCulture, schema);
        return complete.Intersect(pdfCultures).ToArray();
    }
}
