using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using static AssistantQualification.QualificationExistingFixtureReader;

namespace AssistantQualification;

internal static class ExistingFixtureOfflineChecks
{
    public static readonly Action[] All = [Existing_fixture_restores_the_same_ids_in_seed_order,
        Existing_fixture_rejects_a_missing_actor, Existing_fixture_rejects_an_inactive_actor,
        Existing_fixture_rejects_duplicate_form_identity, Existing_fixture_rejects_a_changed_source_version,
        Existing_fixture_rejects_a_missing_catalog_choice, Existing_fixture_rejects_a_foreign_history_subject,
        Existing_fixture_rejects_a_missing_historical_offer, Existing_fixture_rejects_an_unconfirmed_capture,
        Existing_fixture_rejects_a_changed_suggestion_mapping];

    private static void Existing_fixture_restores_the_same_ids_in_seed_order()
    {
        var source = Fixture();
        var (actors, forms) = Reconstruct(source with { Users = source.Users.Reverse().ToArray(), Forms = source.Forms.Reverse().ToArray() });
        Require(actors.Actors.Count == 8 && actors.AudioActors.Count == 2 && actors.Subjects.Count == 2 && actors.Operators.Count == 2);
        for (var index = 0; index < 8; index++)
            Require(actors.Actors[index].UserId == source.Users.Single(user => user.Email == $"actor{index}@example.invalid").Id
                && actors.Actors[index].Alias == $"home-{index}");
        Require(actors.SupervisorRoleId == source.SupervisorRoleId);
        foreach (var form in forms.Forms)
        {
            var row = source.Forms.Single(row => row.DefinitionId == form.DefinitionId);
            var questions = JsonSerializer.Deserialize<FormVersionSchema>(row.SchemaJson, JsonSerializerOptions.Web)!.Questions;
            Require(form.VersionId == row.VersionId && form.Fields.Values.SequenceEqual(questions.Select(field => field.FieldId)));
        }
        var red = source.Items.Single(item => item.Code == "RED");
        Require(forms.Form("control").ChoiceIds["red"] == red.Id);
        Require(forms.Suggestions.Count == 2 && forms.PriorOffers.Count == 2);
        foreach (var suggestion in forms.Suggestions)
        {
            var row = source.Records.Single(record => record.Id == suggestion.SourceRecordId);
            var history = source.Histories.Single(item => item.SessionId == row.SourceReceiptId);
            Require(suggestion.SubjectUserId == row.ActorUserId && suggestion.SourceNumber == history.Number
                && forms.PriorOffers.Single(offer => offer.SubjectUserId == row.ActorUserId).RequestId == history.RequestId);
        }
    }

    private static void Existing_fixture_rejects_a_missing_actor()
    {
        var source = Fixture(); Reject(source with { Users = source.Users.Skip(1).ToArray() });
    }
    private static void Existing_fixture_rejects_an_inactive_actor()
    {
        var source = Fixture(); Reject(source with { Users = source.Users.Select((row, index) => index == 0 ? row with { IsActive = false } : row).ToArray() });
    }
    private static void Existing_fixture_rejects_duplicate_form_identity()
    {
        var source = Fixture(); Reject(source with { Forms = [..source.Forms, source.Forms[0]] });
    }
    private static void Existing_fixture_rejects_a_changed_source_version()
    {
        var source = Fixture(); Reject(source with { Suggestions = [source.Suggestions[0] with { SourceVersionId = Guid.NewGuid() }] });
    }
    private static void Existing_fixture_rejects_a_missing_catalog_choice()
    {
        var source = Fixture(); Reject(source with { Items = source.Items.Where(item => item.Code != "RED").ToArray() });
    }
    private static void Existing_fixture_rejects_a_foreign_history_subject()
    {
        var source = Fixture(); Reject(source with { Histories = source.Histories.Select((row, index) => index == 0 ? row with { LinkedUserId = Guid.NewGuid() } : row).ToArray() });
    }
    private static void Existing_fixture_rejects_a_missing_historical_offer()
    {
        var source = Fixture(); Reject(source with { Histories = source.Histories.Skip(1).ToArray() });
    }
    private static void Existing_fixture_rejects_an_unconfirmed_capture()
    {
        var source = Fixture(); Reject(source with { Histories = source.Histories.Select((row, index) => index == 0 ? row with { Completed = false } : row).ToArray() });
    }
    private static void Existing_fixture_rejects_a_changed_suggestion_mapping()
    {
        var source = Fixture();
        var rows = JsonSerializer.Deserialize<FormSuggestion[]>(source.Suggestions[0].Json, JsonSerializerOptions.Web)!;
        rows[0] = rows[0] with { Mappings = [new(new("Field", Guid.NewGuid()), rows[0].Mappings[0].DestinationFieldId)] };
        Reject(source with { Suggestions = [source.Suggestions[0] with { Json = JsonSerializer.Serialize(rows, JsonSerializerOptions.Web) }] });
    }

    private static Snapshot Fixture()
    {
        var users = new List<UserRow>(); var memberships = new List<MembershipRow>();
        for (var index = 0; index < 8; index++) Add($"actor{index}", index % 2 == 0 ? "es" : "en", "QualificationHome", false);
        memberships.Add(new(users[0].Id, "Supervisor"));
        foreach (var culture in new[] { "es", "en" })
        {
            Add("audio-" + culture, culture, "QualificationAudio", true);
            Add("subject-" + culture, culture, "QualificationSubject", true);
            Add("operator-" + culture, culture, "QualificationOperator", true);
        }
        users.Add(new(Guid.NewGuid(), "inactive@example.invalid", "es", false, false));
        var colors = Guid.NewGuid(); var branches = Guid.NewGuid(); var setId = Guid.NewGuid();
        var forms = new[] { Form("VISIT", ["shortText", "longText", "photo"], setId),
            Form("LOCALVISIT", ["shortText", "longText", "photo", "choice"]),
            Form("MAINT", ["shortText", "longText"]), Form("CONTROL", ["shortText", "longText", "decimal", "date", "boolean", "choice"]) };
        var sourcePlace = Questions(forms[0])[0].FieldId; var targetPlace = Questions(forms[2])[0].FieldId;
        var suggestion = new FormSuggestion(Guid.NewGuid(), forms[2].DefinitionId, "Assistant", "Public fixture instruction.", true, [],
            [new(new("Field", sourcePlace), targetPlace)]) { DestinationVersionId = forms[2].VersionId };
        var set = new SuggestionRow(setId, forms[0].DefinitionId, forms[0].VersionId, JsonSerializer.Serialize(new[] { suggestion }, JsonSerializerOptions.Web));
        var records = new List<RecordRow>(); var histories = new List<HistoryRow>();
        foreach (var subject in users.Where(user => user.Email.StartsWith("subject-", StringComparison.Ordinal)))
        {
            var session = Guid.NewGuid(); var id = Guid.NewGuid(); var number = records.Count + 1;
            records.Add(new(id, forms[0].DefinitionId, forms[0].VersionId, subject.Id, session, number));
            histories.Add(new(session, subject.Id, subject.Id, forms[0].DefinitionId, forms[0].VersionId, id,
                "VISIT-" + number.ToString("D6", CultureInfo.InvariantCulture), true, Guid.NewGuid(), true));
        }
        return new(Guid.NewGuid(), users, memberships, forms, [set],
            [new(Guid.NewGuid(), colors, "RED", true, true), new(Guid.NewGuid(), colors, "BLUE", true, true),
             new(Guid.NewGuid(), branches, "NORTH", true, false)], records, histories);

        void Add(string alias, string culture, string role, bool phone)
        {
            var user = new UserRow(Guid.NewGuid(), alias + "@example.invalid", culture, true, phone);
            users.Add(user); memberships.Add(new(user.Id, role));
        }
        FormRow Form(string prefix, string[] types, Guid? set = null)
        {
            var fieldIds = types.Select(_ => Guid.NewGuid()).ToArray();
            Guid FieldId(int index) => fieldIds[index];
            string Key(int index) => "field_" + fieldIds[index].ToString("N");
            var fields = types.Select((type, index) => new CanonicalFormField(FieldId(index), Key(index),
                type, new Dictionary<string, string> { ["es"] = "Campo", ["en"] = "Field" }, null, false, null, null,
                type == "choice" ? new(prefix == "CONTROL" ? colors : branches) : null)).ToArray();
            return new(Guid.NewGuid(), Guid.NewGuid(), prefix, 1,
                JsonSerializer.Serialize(new FormVersionSchema(1, fields), JsonSerializerOptions.Web), set);
        }
    }
    private static IReadOnlyList<CanonicalFormField> Questions(FormRow row) =>
        JsonSerializer.Deserialize<FormVersionSchema>(row.SchemaJson, JsonSerializerOptions.Web)!.Questions;
    private static void Reject(Snapshot snapshot)
    {
        try { _ = Reconstruct(snapshot); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Existing fixture rejection check failed.");
    }
    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Existing fixture check failed."); }
}
