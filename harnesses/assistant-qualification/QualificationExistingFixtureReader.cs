using System.Data;
using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantQualification;

// Reads the already-created fictitious evaluation fixture; never seeds or changes a row.
internal sealed class QualificationExistingFixtureReader(ApplicationDbContext db)
{
    private static readonly string[] Prefixes = ["VISIT", "LOCALVISIT", "MAINT", "CONTROL"];
    private static readonly string[] Emails = Enumerable.Range(0, 8).Select(index => $"actor{index}@example.invalid")
        .Concat(new[] { "audio-es", "audio-en", "subject-es", "subject-en", "operator-es", "operator-en", "inactive" }
            .Select(alias => alias + "@example.invalid")).ToArray();

    public async Task<(QualificationFixture Actors, QualificationChannelFixture Forms)> ReadAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var users = await db.Users.AsNoTracking().Where(user => Emails.Contains(user.Email!))
            .Select(user => new UserRow(user.Id, user.Email!, user.Culture, user.IsActive,
                user.PhoneNumber != null && user.PhoneNumberConfirmed)).ToArrayAsync(ct);
        var ids = users.Select(user => user.Id).ToArray();
        var memberships = await (from membership in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
            where ids.Contains(membership.UserId)
            select new MembershipRow(membership.UserId, role.Name!)).ToArrayAsync(ct);
        var supervisor = await db.Roles.AsNoTracking().Where(role => role.Name == "Supervisor")
            .Select(role => role.Id).SingleAsync(ct);
        var forms = await (from definition in db.Set<BusinessFormDefinition>().AsNoTracking()
            join version in db.Set<BusinessFormVersion>().AsNoTracking() on definition.ActiveVersionId equals version.Id
            where Prefixes.Contains(definition.Prefix)
            select new FormRow(definition.Id, version.Id, definition.Prefix, version.Number,
                version.SchemaJson, definition.ActiveSuggestionSetId)).ToArrayAsync(ct);
        var definitionIds = forms.Select(form => form.DefinitionId).ToArray();
        var sets = await db.Set<BusinessFormSuggestionSet>().AsNoTracking().Where(set => definitionIds.Contains(set.DefinitionId))
            .Select(set => new SuggestionRow(set.Id, set.DefinitionId, set.SourceVersionId, set.SuggestionsJson)).ToArrayAsync(ct);
        var items = await db.Set<BusinessFormCatalogItem>().AsNoTracking().OrderBy(item => item.Id).Select(item =>
            new ItemRow(item.Id, item.CatalogId, item.Code, item.IsActive, item.TextEn != null)).Take(4).ToArrayAsync(ct);
        var records = await db.Set<BusinessFormRecord>().AsNoTracking().Where(record => ids.Contains(record.ActorUserId)
                && definitionIds.Contains(record.DefinitionId))
            .Select(record => new RecordRow(record.Id, record.DefinitionId, record.VersionId, record.ActorUserId,
                record.SourceReceiptId, record.Number)).ToArrayAsync(ct);
        var histories = await (from session in db.Set<WhatsAppFlowSession>().AsNoTracking()
            join contact in db.Set<WhatsAppContact>().AsNoTracking() on session.ContactId equals contact.Id
            join outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking() on session.Id equals outgoing.FlowSessionId
            where ids.Contains(session.UserId) && definitionIds.Contains(session.DefinitionId)
            select new HistoryRow(session.Id, session.UserId, contact.UserId, session.DefinitionId, session.VersionId,
                session.CompletedRecordId, session.CompletedNumber, session.CaptureState == WhatsAppFlowCaptureState.Completed,
                outgoing.RequestId, outgoing.Kind == WhatsAppOutgoingMessageKind.Flow
                    && outgoing.State == WhatsAppOutgoingMessageState.Reviewed && outgoing.WaMessageId == null)).ToArrayAsync(ct);
        return Reconstruct(new(supervisor, users, memberships, forms, sets, items, records, histories));
    }

    internal static (QualificationFixture Actors, QualificationChannelFixture Forms) Reconstruct(Snapshot snapshot)
    {
        Check(snapshot.SupervisorRoleId != Guid.Empty && snapshot.Users.Count == 15
            && snapshot.Users.Select(user => user.Id).Distinct().Count() == 15
            && snapshot.Users.Select(user => user.Email).Distinct(StringComparer.Ordinal).Count() == 15);
        Check(snapshot.Memberships.All(row => snapshot.Users.Any(user => user.Id == row.UserId)));
        var home = Enumerable.Range(0, 8).Select(index => Actor($"actor{index}", $"home-{index}",
            index % 2 == 0 ? "es" : "en", "QualificationHome", false, index == 0)).ToArray();
        var audio = Cultures.Select(culture => Actor("audio-" + culture, "audio-" + culture, culture, "QualificationAudio", true)).ToArray();
        var subjects = Cultures.Select(culture => Actor("subject-" + culture, "subject-" + culture, culture, "QualificationSubject", true)).ToArray();
        var operators = Cultures.Select(culture => Actor("operator-" + culture, "operator-" + culture, culture, "QualificationOperator", true)).ToArray();
        var inactive = One(snapshot.Users, user => user.Email == "inactive@example.invalid");
        Check(!inactive.IsActive && inactive.Culture == "es");
        var actors = new QualificationFixture(home, "Supervisor")
            { SupervisorRoleId = snapshot.SupervisorRoleId, AudioActors = audio, Subjects = subjects, Operators = operators };

        Check(snapshot.Forms.Count == 4 && snapshot.Forms.Select(form => form.DefinitionId).Distinct().Count() == 4
            && snapshot.Forms.Select(form => form.VersionId).Distinct().Count() == 4);
        var forms = new List<QualificationForm>();
        var catalogs = new Dictionary<string, Guid>(StringComparer.Ordinal);
        AddForm("VISIT", "visit", ["place", "notes", "photo"], ["shortText", "longText", "photo"]);
        AddForm("LOCALVISIT", "local-visit", ["place", "notes", "photo", "branch"], ["shortText", "longText", "photo", "choice"]);
        AddForm("MAINT", "maintenance", ["place", "notes"], ["shortText", "longText"]);
        AddForm("CONTROL", "control", ["text", "notes", "amount", "day", "confirmed", "color"],
            ["shortText", "longText", "decimal", "date", "boolean", "choice"]);
        Check(forms.SelectMany(form => form.Fields.Values).Distinct().Count() == forms.Sum(form => form.Fields.Count));
        var control = forms.Single(form => form.Alias == "control");
        var colorItems = snapshot.Items.Where(item => item.CatalogId == catalogs["control"]).ToArray();
        var branchItems = snapshot.Items.Where(item => item.CatalogId == catalogs["local-visit"]).ToArray();
        Check(snapshot.Items.Count == 3 && colorItems.Length == 2 && colorItems.All(item => item.IsActive && item.HasEnglish)
            && branchItems.Length == 1 && branchItems[0].IsActive && !branchItems[0].HasEnglish && branchItems[0].Code == "NORTH");
        var choices = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var code in new[] { "RED", "BLUE" })
        {
            var item = One(colorItems, item => item.Code == code);
            Check(item.Id != Guid.Empty);
            choices.Add(code.ToLowerInvariant(), item.Id);
        }
        Check(choices.Values.Distinct().Count() == 2);
        forms[forms.IndexOf(control)] = control with { ChoiceIds = choices };
        var visit = forms.Single(form => form.Alias == "visit");
        var maintenance = forms.Single(form => form.Alias == "maintenance");
        var visitRow = One(snapshot.Forms, row => row.DefinitionId == visit.DefinitionId);
        Check(snapshot.Suggestions.Count == 1 && snapshot.Forms.Where(row => row.DefinitionId != visit.DefinitionId)
            .All(row => row.SuggestionSetId == null));
        var set = snapshot.Suggestions[0];
        Check(set.Id != Guid.Empty && set.Id == visitRow.SuggestionSetId && set.DefinitionId == visit.DefinitionId
            && set.SourceVersionId == visit.VersionId);
        var entries = JsonSerializer.Deserialize<FormSuggestion[]>(set.Json, JsonSerializerOptions.Web);
        Check(entries is { Length: 1 });
        var suggestion = entries![0];
        Check(suggestion.Id != Guid.Empty && suggestion.IsActive && suggestion.Mode == "Assistant"
            && !string.IsNullOrWhiteSpace(suggestion.When) && suggestion.DestinationDefinitionId == maintenance.DefinitionId
            && suggestion.DestinationVersionId == maintenance.VersionId && suggestion.Conditions.Count == 0
            && suggestion.Mappings.Count == 1 && suggestion.Mappings[0].Source.Kind == "Field"
            && suggestion.Mappings[0].Source.FieldId == visit.Fields["place"]
            && suggestion.Mappings[0].DestinationFieldId == maintenance.Fields["place"]);
        Check(snapshot.Records.Count == 2 && snapshot.Histories.Count == 2
            && snapshot.Records.Select(record => record.Id).Distinct().Count() == 2
            && snapshot.Histories.Select(history => history.RequestId).Distinct().Count() == 2);
        var captured = new List<QualificationSuggestion>(); var offers = new List<QualificationOffer>();
        foreach (var subject in subjects)
        {
            var record = One(snapshot.Records, record => record.ActorUserId == subject.UserId);
            var history = One(snapshot.Histories, history => history.SubjectUserId == subject.UserId);
            Check(record.Id != Guid.Empty && record.DefinitionId == visit.DefinitionId && record.VersionId == visit.VersionId
                && record.SourceReceiptId == history.SessionId && record.Number > 0
                && history.LinkedUserId == subject.UserId && history.RecordId == record.Id && history.Completed
                && history.DefinitionId == visit.DefinitionId && history.VersionId == visit.VersionId
                && history.Number == "VISIT-" + record.Number.ToString("D6", CultureInfo.InvariantCulture)
                && history.RequestId != Guid.Empty && history.IsReviewedFixtureOffer);
            captured.Add(new(subject.UserId, record.Id, history.Number!, suggestion.Id, maintenance.DefinitionId));
            offers.Add(new(subject.UserId, history.RequestId, visit.DefinitionId, visit.VersionId));
        }
        return (actors, new(forms, captured, offers));

        QualificationActor Actor(string emailAlias, string alias, string culture, string role, bool phone, bool supervisor = false)
        {
            var row = One(snapshot.Users, user => user.Email == emailAlias + "@example.invalid");
            var memberships = snapshot.Memberships.Where(membership => membership.UserId == row.Id).Select(membership => membership.Role).ToArray();
            Check(row.Id != Guid.Empty && row.IsActive && row.Culture == culture && row.HasConfirmedPhone == phone
                && memberships.Length == (supervisor ? 2 : 1) && memberships.Contains(role, StringComparer.Ordinal)
                && (!supervisor || memberships.Contains("Supervisor", StringComparer.Ordinal)));
            return new(row.Id, alias, culture, role);
        }
        void AddForm(string prefix, string alias, string[] keys, string[] types)
        {
            var row = One(snapshot.Forms, row => row.Prefix == prefix);
            var schema = JsonSerializer.Deserialize<FormVersionSchema>(row.SchemaJson, JsonSerializerOptions.Web);
            Check(row.DefinitionId != Guid.Empty && row.VersionId != Guid.Empty && row.VersionNumber == 1
                && schema is { Format: 1 } && schema.Questions.Count == keys.Length);
            var fields = new Dictionary<string, Guid>(StringComparer.Ordinal);
            for (var index = 0; index < keys.Length; index++)
            {
                var field = schema!.Questions[index];
                Check(field.FieldId != Guid.Empty && field.Type == types[index]
                    && field.Key == "field_" + field.FieldId.ToString("N"));
                fields.Add(keys[index], field.FieldId);
                if (field.Type == "choice")
                {
                    Check(field.Source?.Catalog is { } id && id != Guid.Empty);
                    catalogs.Add(alias, field.Source!.Catalog!.Value);
                }
            }
            forms.Add(new(row.DefinitionId, row.VersionId, alias, fields));
        }
    }

    private static readonly string[] Cultures = ["es", "en"];
    private static T One<T>(IEnumerable<T> rows, Func<T, bool> matches)
    {
        var found = rows.Where(matches).Take(2).ToArray();
        Check(found.Length == 1);
        return found[0];
    }
    private static void Check(bool valid)
    {
        if (!valid) throw new InvalidOperationException("Existing qualification fixture is incomplete or inconsistent.");
    }

    internal sealed record UserRow(Guid Id, string Email, string Culture, bool IsActive, bool HasConfirmedPhone);
    internal sealed record MembershipRow(Guid UserId, string Role);
    internal sealed record FormRow(Guid DefinitionId, Guid VersionId, string Prefix, long VersionNumber, string SchemaJson, Guid? SuggestionSetId);
    internal sealed record SuggestionRow(Guid Id, Guid DefinitionId, Guid SourceVersionId, string Json);
    internal sealed record ItemRow(Guid Id, Guid CatalogId, string Code, bool IsActive, bool HasEnglish);
    internal sealed record RecordRow(Guid Id, Guid DefinitionId, Guid VersionId, Guid ActorUserId, Guid SourceReceiptId, long Number);
    internal sealed record HistoryRow(Guid SessionId, Guid SubjectUserId, Guid? LinkedUserId, Guid DefinitionId, Guid VersionId,
        Guid? RecordId, string? Number, bool Completed, Guid RequestId, bool IsReviewedFixtureOffer);
    internal sealed record Snapshot(Guid SupervisorRoleId, IReadOnlyList<UserRow> Users, IReadOnlyList<MembershipRow> Memberships,
        IReadOnlyList<FormRow> Forms, IReadOnlyList<SuggestionRow> Suggestions, IReadOnlyList<ItemRow> Items,
        IReadOnlyList<RecordRow> Records, IReadOnlyList<HistoryRow> Histories);
}
