using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class FormApprovalContinuationFixture
{
    public static async Task<FormApprovalContinuationSeed> AddAsync(ApplicationDbContext db, Guid firstTaskId,
        Guid deciderId, Guid otherDeciderId, CancellationToken ct)
    {
        var first = await db.Set<BusinessFormTask>().SingleAsync(task => task.Id == firstTaskId, ct);
        var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == first.DefinitionId, ct);
        var version = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == definition.ActiveVersionId, ct);
        var originalRules = await db.Set<BusinessFormRuleSet>().SingleAsync(value => value.Id == first.RuleSetId, ct);
        var originalRun = await db.Set<BusinessFormActionRun>().SingleAsync(value => value.Id == first.RunId, ct);
        var otherRole = await db.UserRoles.Where(link => link.UserId == otherDeciderId).Select(link => link.RoleId).SingleAsync(ct);
        var tiedAt = first.RequestedAtUtc.AddHours(1);
        var second = AddTask(first.AuthorUserId, first.RoleId, tiedAt, originalRules, originalRun.RuleId, originalRun.ActionId);
        var third = AddTask(first.AuthorUserId, first.RoleId, tiedAt, originalRules, originalRun.RuleId, originalRun.ActionId);
        var last = AddTask(first.AuthorUserId, first.RoleId, tiedAt.AddHours(1), originalRules, originalRun.RuleId, originalRun.ActionId);
        var own = AddTask(deciderId, first.RoleId, tiedAt, originalRules, originalRun.RuleId, originalRun.ActionId);
        var otherAction = new FormRuleAction(Guid.NewGuid(), "RequestApproval", otherRole, null, false);
        var otherRule = new FormRule(Guid.NewGuid(), "Received", [], [otherAction]);
        var otherRules = BusinessFormRuleSet.Create(definition.Id, originalRules.Revision + 1, originalRules.OriginalCulture,
            FormDraftPolicy.Serialize(new[] { otherRule }), true, originalRules.QuestionSchemaJson);
        db.Set<BusinessFormRuleSet>().Add(otherRules);
        var other = AddTask(first.AuthorUserId, otherRole, tiedAt, otherRules, otherRule.Id, otherAction.Id);
        await db.SaveChangesAsync(ct);
        var ties = new[] { second.Id, third.Id }.Order().ToArray();
        return new(definition.Id, [first.Id, ..ties, last.Id], own.Id, other.Id, deciderId, definition.Prefix);

        BusinessFormTask AddTask(Guid author, Guid role, DateTime at, BusinessFormRuleSet rules, Guid ruleId, Guid actionId)
        {
            var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), author,
                Guid.NewGuid(), at, "[]", rules.Id);
            if (record.MarkPendingApproval().IsFailure) throw new InvalidOperationException("The continuation fixture could not open its response.");
            db.Set<BusinessFormRecord>().Add(record);
            db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(record.SourceReceiptId,
                definition.Id, version.Id, "test-continuation", record.Id, record.Number, at));
            var snapshot = FormDraftPolicy.Serialize(new FormActionSnapshot(role, null, null, []));
            var run = BusinessFormActionRun.Create(definition.Id, record.Id, rules.Id, ruleId, actionId,
                "Received", "RequestApproval", 0, null, snapshot, true, at);
            db.Set<BusinessFormActionRun>().Add(run);
            var task = BusinessFormTask.Open(definition.Id, record.Id, rules.Id, run.Id, role, author, at);
            db.Set<BusinessFormTask>().Add(task);
            return task;
        }
    }
}

internal sealed record FormApprovalContinuationSeed(Guid DefinitionId, Guid[] OrderedTaskIds, Guid OwnTaskId,
    Guid OtherRoleTaskId, Guid DeciderId, string Prefix);
