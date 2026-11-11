using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace AssistantQualification;

// Fictitious accounts for the authoring sets only (IA1-A/IA3-A), after QualificationSeed and QualificationFormSeed in the same
// isolated database. The existing sets never create them: their users.summary facts count exactly the base accounts.
internal sealed class QualificationAuthoringSeed(IUnitOfWork unitOfWork, IUserRepository users, IRoleRepository roles,
    IEnumerable<IAssistantCapabilitySource> sources, IFormCatalogService catalogs, QualificationCurrentUser currentUser)
{
    private static readonly string[] Cultures = ["es", "en"];
    internal static readonly string[] Building = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
        BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
    // Stage 2 (spreadsheet sets only): the Manual catalog a spreadsheet is imported into, with a required field.
    internal static readonly QualificationCatalogName ImportCatalog = new("suppliers", "Proveedores", "Suppliers");
    internal static readonly (string Key, string Spanish, string English, bool Required)[] ImportFields =
        [("rubro", "Rubro", "Category", true), ("ciudad", "Ciudad", "City", false)];

    public async Task<QualificationFixture> CreateAsync(QualificationFixture fixture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var home = sources.SelectMany(source => source.Definitions).Where(definition => QualificationSeed.HomeCapabilities.Contains(definition.Id))
            .SelectMany(definition => definition.RequiredPermissions.Actor).Append(ArtificialIntelligencePermissions.AssistantUse);
        var authors = new List<QualificationActor>();
        var channelAuthors = new List<QualificationActor>();
        var voiceAuthors = new List<QualificationActor>();
        var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Home: the five reads plus designing forms and managing catalogs, so Home offers the authoring and H4 capabilities.
            await roles.CreateAsync("QualificationAuthor", "Home with fictitious form and catalog authoring",
                home.Concat(Building).Distinct(StringComparer.Ordinal).ToArray(), token);
            // WhatsApp automatic: the recipient's own conversation, where Actor equals Subject.
            await roles.CreateAsync("QualificationChannelAuthor", "Own automatic conversation with fictitious form and catalog authoring",
                [..Building, BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead], token);
            for (var index = 0; index < 4; index++)
            {
                var culture = Cultures[index % 2];
                var author = await users.CreateAsync(Email.Create($"author{index}@example.invalid").Value, null, false,
                    $"Qualification author {index}", culture, token);
                await users.SetRolesAsync(author.Id, ["QualificationAuthor"], token);
                authors.Add(new(author.Id, $"author-{index}", culture, "QualificationAuthor"));
            }
            foreach (var culture in Cultures)
            {
                var recipient = await users.CreateAsync(Email.Create($"author-subject-{culture}@example.invalid").Value, null, false,
                    $"Authoring recipient {culture}", culture, token);
                await users.SetRolesAsync(recipient.Id, ["QualificationChannelAuthor"], token);
                channelAuthors.Add(new(recipient.Id, $"author-subject-{culture}", culture, "QualificationChannelAuthor"));
                // IA3-A by voice: another recipient of the same role dictates, with a daily quota of their own.
                var voice = await users.CreateAsync(Email.Create($"author-voice-{culture}@example.invalid").Value, null, false,
                    $"Dictating recipient {culture}", culture, token);
                await users.SetRolesAsync(voice.Id, ["QualificationChannelAuthor"], token);
                voiceAuthors.Add(new(voice.Id, $"author-voice-{culture}", culture, "QualificationChannelAuthor"));
            }
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (result.IsFailure) Fail("authoring-seed", result.Error);
        return fixture with { Authors = authors, ChannelAuthors = channelAuthors, VoiceAuthors = voiceAuthors };
    }

    // Stage 2, only for the spreadsheet sets: the Manual catalog a spreadsheet is imported into, created by the fictitious operator through
    // the real catalog service like the form seed's catalogs, empty and with a required field. Every other set reads exactly the base
    // catalogs, so their authoring reads do not change. A new field can never be born required (`FormCatalogFieldRecorder`): the catalog
    // is created with every field optional and a second save marks the required ones, which an empty catalog accepts.
    public async Task<QualificationChannelFixture> CreateImportCatalogAsync(QualificationFixture actors, QualificationChannelFixture forms,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(forms);
        currentUser.Set(actors.Operators.Single(actor => actor.Culture == "es").UserId);
        var name = Texts(ImportCatalog.Spanish, ImportCatalog.English);
        var description = Texts("Proveedores de prueba", "Fixture suppliers");
        var created = await catalogs.CreateManualAsync(new(name, description, [], null,
            ImportFields.Select((field, position) => Field(field.Key, null, field, required: false, position)).ToArray()), cancellationToken);
        if (created.IsFailure) Fail("import-catalog", created.Error);
        var fieldIds = created.Value.Catalog.Fields!.ToDictionary(field => field.Key, field => field.Id, StringComparer.Ordinal);
        var required = await catalogs.UpdateManualAsync(created.Value.Catalog.Id, new(created.Value.Catalog.Revision, name, description, [],
            Fields: ImportFields.Select((field, position) => Field(null, fieldIds[field.Key], field, field.Required, position)).ToArray()),
            cancellationToken);
        if (required.IsFailure) Fail("import-catalog-required", required.Error);
        return forms with { Catalogs = [..forms.Catalogs, new(ImportCatalog.Alias, created.Value.Catalog.Id,
            new Dictionary<string, string> { ["es"] = ImportCatalog.Spanish, ["en"] = ImportCatalog.English })] };
    }

    private static FormCatalogFieldSaveRequest Field(string? localId, Guid? id, (string Key, string Spanish, string English, bool Required) field,
        bool required, int position) =>
        new(localId, id, field.Key, Texts(field.Spanish, field.English), "text", null, required, false, null, null, 80, "data", position, true);

    // A failed seed prints only its fixed step, the stable error code and the field paths, never a message or a value.
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string step, Error error)
    {
        var fields = error is ValidationError validation ? string.Join(",", validation.Errors.Keys.Order(StringComparer.Ordinal)) : "-";
        Console.WriteLine($"qualification-seed-failed {step}: {error.Code} fields={fields}");
        throw new InvalidOperationException("Qualification " + step + " failed.");
    }

    private static Dictionary<string, string?> Texts(string es, string en) => new() { ["es"] = es, ["en"] = en };
}
