using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

/// <summary>
/// DOC7-A: a published form with synthetic responses written straight to PostgreSQL, with the instant, the person, the state, the
/// typed values (several options of «elegir varias» in their positions) and the catalog references each test needs; people with a
/// role of their own, a name and the time zone of their profile.
/// </summary>
internal static class FormDashboardScenario
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task<Guid> PersonAsync(ApiFactory factory, string name, IReadOnlyCollection<string> permissions, CancellationToken ct,
        string timeZoneId = SystemSettings.InitialTimeZoneId) =>
        await factory.InTransactionAsync(async services =>
        {
            var role = "dashboard-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, permissions, ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("dashboard")).Value, null, false, name, "es", ct, timeZoneId);
            await users.SetRolesAsync(user.Id, [role], ct);
            return user.Id;
        });

    public static HttpClient Client(ApiFactory factory, Guid person, string culture = "es")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, person.ToString());
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        return client;
    }

    public static CanonicalFormField Field(string type, string es, string en, IReadOnlyList<CanonicalFormChoice>? options = null, Guid? catalog = null)
    {
        var id = Guid.NewGuid();
        return new(id, "field_" + id.ToString("N"), type, new Dictionary<string, string> { ["es"] = es, ["en"] = en }, null, false, null, options,
            Source: catalog is { } catalogId ? new(catalogId) : null);
    }

    public static CanonicalFormChoice Choice(string es, string en) => new(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = es, ["en"] = en });

    /// <summary>A Manual catalog named «Bancos …» (unique) in Spanish and «Banks» in English.</summary>
    public static async Task<Guid> CatalogAsync(ApiFactory factory, CancellationToken ct)
    {
        var names = new Dictionary<string, string> { ["es"] = "Bancos " + Guid.NewGuid().ToString("N"), ["en"] = "Banks" };
        var catalog = BusinessFormCatalog.CreateManual("es", names, names);
        await factory.ExecuteDbContextAsync(async db => { db.Set<BusinessFormCatalog>().Add(catalog); await db.SaveChangesAsync(ct); });
        return catalog.Id;
    }

    public static async Task<Guid> FormAsync(ApiFactory factory, Guid publisher, IReadOnlyList<CanonicalFormField> fields, CancellationToken ct)
    {
        var prefix = "D" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var definition = BusinessFormDefinition.Create();
        var version = BusinessFormVersion.Create(definition.Id, 1, "dashboard");
        var name = JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = prefix + " Pagos", ["en"] = prefix + " Payments" });
        var submit = JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" });
        Assert.True(definition.SetDraft(1, prefix + " Pagos", name, prefix, "es", submit,
            JsonSerializer.Serialize(new FormQuestionSchema(1, fields), SerializerOptions), fields.Count, true).IsSuccess);
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), SerializerOptions));
        version.SealPresentation(name, submit, "es", fields.Count);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, publisher, ["es", "en"]));
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(ct);
        });
        return definition.Id;
    }

    /// <summary>Writes the responses in one save and returns their ids, in order.</summary>
    public static async Task<IReadOnlyList<Guid>> AddAsync(ApiFactory factory, Guid definitionId, IReadOnlyList<FormDashboardRecord> records, CancellationToken ct) =>
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == definitionId, ct);
            var versionId = definition.ActiveVersionId!.Value;
            var ids = new List<Guid>();
            foreach (var seed in records)
            {
                var receiptId = Guid.NewGuid();
                var values = JsonSerializer.Serialize(seed.Answers.Select(answer => new
                {
                    fieldId = answer.Field.FieldId,
                    value = (object?)answer.Number ?? (object?)answer.Choice ?? (object?)answer.Flag ?? (object?)answer.Date ?? (object?)answer.Choices ?? answer.Item!.ItemId,
                }), SerializerOptions);
                var record = BusinessFormRecord.Create(definitionId, versionId, definition.AllocateRecordNumber(), seed.Actor, receiptId, seed.SubmittedAtUtc, values);
                ids.Add(record.Id);
                db.Set<BusinessFormRecord>().Add(record);
                db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receiptId, definitionId, versionId, "dashboard", record.Id, record.Number,
                    seed.SubmittedAtUtc));
                foreach (var answer in seed.Answers)
                {
                    if (answer.Choices is { } choices)
                        for (var position = 0; position < choices.Count; position++)
                            db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, definitionId, answer.Field.FieldId, answer.Field.Type,
                                choiceId: choices[position], position: position));
                    else
                        db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, definitionId, answer.Field.FieldId, answer.Field.Type,
                            decimalValue: answer.Number, dateValue: answer.Date, booleanValue: answer.Flag, choiceId: answer.Choice ?? answer.Item?.ItemId));
                    if (answer.Item is { } item)
                        db.Set<BusinessFormRecordReference>().Add(BusinessFormRecordReference.Create(record.Id, definitionId, answer.Field.FieldId, item.CatalogId, 1,
                            item.ItemId, item.Es, null, "Bancos", JsonSerializer.Serialize(new Dictionary<string, object>
                            {
                                ["es"] = new { text = item.Es, detail = (string?)null, catalogName = "Bancos" },
                                ["en"] = new { text = item.En, detail = (string?)null, catalogName = "Banks" },
                            })));
                }
                if (seed.State is FormRecordState.PendingApproval or FormRecordState.Approved or FormRecordState.Rejected)
                    Assert.True(record.MarkPendingApproval().IsSuccess);
                if (seed.State is FormRecordState.Approved or FormRecordState.Rejected)
                    Assert.True(record.CompleteApproval(seed.State == FormRecordState.Approved).IsSuccess);
                if (seed.State == FormRecordState.Voided) Assert.True(record.Void(seed.Actor, "Cargada dos veces", seed.SubmittedAtUtc).IsSuccess);
            }
            await db.SaveChangesAsync(ct);
            return (IReadOnlyList<Guid>)ids;
        });

    /// <summary>
    /// Leaves a number answer with a value a C# decimal cannot hold, as PostgreSQL's numeric admits it («Uno malo no frena al resto»):
    /// the largest decimal plus one.
    /// </summary>
    public static Task PastTheDecimalRangeAsync(ApiFactory factory, Guid recordId, Guid fieldId, CancellationToken ct) =>
        factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlAsync(
            $"""UPDATE "BusinessFormRecordValues" SET "DecimalValue" = 79228162514264337593543950335::numeric + 1 WHERE "RecordId" = {recordId} AND "FieldId" = {fieldId}""",
            ct));
}

internal sealed record FormDashboardRecord(Guid Actor, DateTime SubmittedAtUtc, IReadOnlyList<FormDashboardAnswer> Answers,
    FormRecordState State = FormRecordState.Received);

internal sealed record FormDashboardAnswer(CanonicalFormField Field, decimal? Number = null, Guid? Choice = null, bool? Flag = null, FormDashboardItem? Item = null,
    DateOnly? Date = null, IReadOnlyList<Guid>? Choices = null);

internal sealed record FormDashboardItem(Guid CatalogId, Guid ItemId, string Es, string En);
