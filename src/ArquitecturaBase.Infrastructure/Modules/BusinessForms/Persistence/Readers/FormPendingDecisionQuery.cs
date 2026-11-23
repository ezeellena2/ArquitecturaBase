using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Infrastructure.Persistence;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

/// <summary>
/// What awaits the decision of a person, in one place: «Formularios» (its count, its «Por aprobar» and its folders) and «Diseñar
/// formularios» (FU2-A) compose it into their own statement, so EF translates it as a subquery of theirs.
/// </summary>
internal static class FormPendingDecisionQuery
{
    /// <summary>
    /// The open tasks of a current role of <paramref name="actorUserId"/>, with an active account, that are not of a response they
    /// submitted (unless the rule lets its author decide), corrected or passed from a document (F4).
    /// </summary>
    internal static IQueryable<BusinessFormTask> AwaitingDecisionOf(ApplicationDbContext db, Guid actorUserId) =>
        db.Set<BusinessFormTask>().Where(task => task.State == FormTaskState.Open && (task.AuthorMayDecide || task.AuthorUserId != actorUserId
            && !db.Set<BusinessFormRecord>().Any(record => record.Id == task.RecordId && record.CorrectedByUserId == actorUserId)
            && !db.Set<FormReceivedDocument>().Any(document => document.RecordId == task.RecordId && document.ReviewedByUserId == actorUserId))
            && db.Users.Any(user => user.Id == actorUserId && user.IsActive)
            && db.UserRoles.Any(link => link.UserId == actorUserId && link.RoleId == task.RoleId));
}
