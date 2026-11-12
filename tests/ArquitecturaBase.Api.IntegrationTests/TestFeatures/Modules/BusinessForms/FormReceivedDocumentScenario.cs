using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// Task 7 with PostgreSQL: a host whose private store is in memory and whose engine is scripted; two published forms of its own, named
// differently in each language, «Pagos» with reading on and «Gastos» without it (each with an amount, an operation number and the
// receipt as a document question); the person who sends documents, a reviewer (who also reads responses) and someone who only sees the
// inbox; the documents arrive through the channel port, as a channel registers them. Everything goes through the host, so a scenario can
// have a database of its own (deleted when it ends) for what counts all the forms or needs data no other test may add. A test may add
// «Cobros» (choices of its own and from catalogs), «Depósitos» (branches filtered by the person's zone), «Compras» (a supplier among as
// many as it says), a copy of «Comprobante de transferencia», newer versions of a form, or documents written straight to PostgreSQL.
internal sealed class FormReceivedDocumentScenario : IAsyncDisposable
{
    public const string Channel = "whatsapp";
    public required ApiFactory Factory { get; init; }
    public required WebApplicationFactory<Program> Host { get; init; }
    public required bool Isolated { get; init; }
    public required AssistantCopyFiles Store { get; init; }
    public required ScriptedDocumentOperations Engine { get; init; }
    public required Guid Publisher { get; init; }
    public required FormReceivedDocumentScenarioForm Form { get; init; }
    public required FormReceivedDocumentScenarioForm Other { get; init; }
    public required Guid Sender { get; init; }
    public required Guid Reviewer { get; init; }
    public required Guid Reader { get; init; }

    public static async Task<FormReceivedDocumentScenario> CreateAsync(ApiFactory factory, CancellationToken ct, bool isolated = false)
    {
        var store = new AssistantCopyFiles();
        var engine = new ScriptedDocumentOperations();
        var connectionString = isolated ? factory.NewDatabaseConnectionString("form_documents") : null;
        var host = factory.WithWebHostBuilder(builder =>
        {
            if (connectionString is not null) builder.UseSetting("ConnectionStrings:appdb", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPrivateFileStore>();
                services.AddSingleton<IPrivateFileStore>(store);
                services.RemoveAll<IAssistantOperations>();
                services.AddSingleton<IAssistantOperations>(engine);
            });
        });
        if (isolated)
        {
            await Scope(host, services => services.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(ct));
            await host.Services.SeedDatabaseAsync(ct);
        }
        var publisher = await PersonAsync(host, "Publica Documentos", [BusinessFormsPermissions.Publish], "es", ct);
        return new FormReceivedDocumentScenario
        {
            Factory = factory,
            Host = host,
            Isolated = isolated,
            Store = store,
            Engine = engine,
            Publisher = publisher,
            Form = await FormAsync(factory, host, publisher, "Pagos", "Payments", true, ct),
            Other = await FormAsync(factory, host, publisher, "Gastos", "Expenses", null, ct),
            Sender = await PersonAsync(host, "Juan Remitente", [BusinessFormsPermissions.RecordsCreate], "es", ct),
            Reviewer = await PersonAsync(host, "Ana Revisora",
                [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview, BusinessFormsPermissions.RecordsRead], "es", ct),
            Reader = await PersonAsync(host, "Luis Lector", [BusinessFormsPermissions.DocumentsRead], "es", ct),
        };
    }

    public HttpClient Client(Guid person, string culture = "es")
    {
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, person.ToString());
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        return client;
    }

    /// <summary>Another person of this scenario's database, with a role of their own and the language of their account.</summary>
    public Task<Guid> PersonAsync(string name, IReadOnlyCollection<string> permissions, CancellationToken ct, string culture = "es") =>
        PersonAsync(Host, name, permissions, culture, ct);

    /// <summary>
    /// A document the sender sent (a PDF of its own unless one is given), with its copy in the channel's store: read, with the engine's
    /// proposal (by default «sure» for «Pagos» with the amount and, doubtful, the operation; a null amount was not read), or not read for
    /// the reason given. It arrives now, or when the test says.
    /// </summary>
    public async Task<FormReceivedDocumentSent> SendAsync(CancellationToken ct, string? sourceId = null, string? amount = "150000",
        string operation = "778899", StructuredDocumentNotRead? notRead = null, DateTime? replyExpiresAtUtc = null, string outcome = "sure",
        byte[]? content = null, StructuredDocumentReadability readability = StructuredDocumentReadability.Readable, Guid? sender = null,
        IReadOnlyList<FormReceivedDocumentScenarioForm>? candidates = null, AssistantObservedProposal? proposal = null, DateTime? receivedAtUtc = null)
    {
        sourceId ??= "wamid." + Guid.NewGuid().ToString("N");
        content ??= Pdf();
        var from = sender ?? Sender;
        var key = "channel/documents/" + Guid.NewGuid().ToString("N") + ".pdf";
        Store.Put(key, content);
        var forms = candidates ?? [Form];
        var request = new StructuredDocumentRegistrationRequest(Channel, sourceId, from, "es", receivedAtUtc ?? Factory.Clock.GetUtcNow().UtcDateTime,
            new(key, "comprobante.pdf", "application/pdf", content.LongLength, Sha256(content), 1))
        {
            NotRead = notRead,
            Rejection = notRead == StructuredDocumentNotRead.Rejected ? StructuredAttachmentRejection.Protected : null,
            Reading = notRead is null ? new StructuredDocumentReading(readability, readability == StructuredDocumentReadability.Readable ? null
                : StructuredDocumentProblem.Blurry, "es", $"| Monto | $ {amount ?? "—"} |\n| Operación | {operation} |\n| Fecha | 11/11/2026 |", false) : null,
            Proposal = notRead is not null ? null
                : proposal ?? Proposal(forms, from, outcome, form => [(form.AmountId, "decimal", amount, false), (form.OperationId, "shortText", operation, true)]),
            ReplyExpiresAtUtc = replyExpiresAtUtc,
        };
        await using var scope = Host.Services.CreateAsyncScope();
        var reply = await scope.ServiceProvider.GetRequiredService<IStructuredDocumentChannel>().RegisterAsync(request, ct);
        return new(reply, content, request);
    }

    /// <summary>
    /// Documents written straight to PostgreSQL, as the inbox keeps them, each from its sender and, with a form, proposed for its active
    /// version (with a proposal nobody can read back: the inbox shows it without data) and for the team at once; without one, unclassified.
    /// </summary>
    public Task SeedDocumentsAsync(IReadOnlyList<(Guid Sender, FormReceivedDocumentScenarioForm? Form)> documents, CancellationToken ct,
        DateTime? receivedAtUtc = null) => DbAsync(async db =>
    {
        // They arrive when the test says, but are kept as long as from now, as registering them keeps them: never expired for the purge of
        // another test of the shared database.
        var now = Factory.Clock.GetUtcNow().UtcDateTime;
        foreach (var (sender, form) in documents)
        {
            var id = Guid.CreateVersion7();
            var document = FormReceivedDocument.Receive(id, Channel, "seed." + id.ToString("N"), sender, receivedAtUtc ?? now,
                "forms/documents/" + id.ToString("N") + ".pdf", "comprobante.pdf", "application/pdf", 1, Sha256(id.ToByteArray()), 1, null).Value;
            Assert.True(document.Describe(null, null, "").IsSuccess);
            Assert.True((form is null ? document.LeaveUnclassified("quota", null, now) : document.Propose(form.DefinitionId, form.VersionId, [1], true)).IsSuccess);
            db.Set<FormReceivedDocument>().Add(document);
        }
        await db.SaveChangesAsync(ct);
        return true;
    });

    /// <summary>The port, in a scope of the host, as a channel calls it.</summary>
    public async Task<T> PortAsync<T>(Func<IStructuredDocumentChannel, Task<T>> call)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<IStructuredDocumentChannel>());
    }

    public Task<T> ScopeAsync<T>(Func<IServiceProvider, Task<T>> call) => Scope(Host, call);

    /// <summary>The scenario's database, in a scope of the host.</summary>
    public Task<T> DbAsync<T>(Func<ApplicationDbContext, Task<T>> action) => Scope(Host, services => action(services.GetRequiredService<ApplicationDbContext>()));

    public Task InTransactionAsync(Func<IServiceProvider, Task> action, CancellationToken ct) =>
        InTransactionAsync(Host, async services => { await action(services); return true; }, ct);

    public Task SetActiveAsync(Guid person, bool active, CancellationToken ct) =>
        InTransactionAsync(services => services.GetRequiredService<IUserRepository>().SetActiveAsync(person, active, ct), ct);

    /// <summary>Another published form like «Pagos», with its reading on, off, or without one.</summary>
    public Task<FormReceivedDocumentScenarioForm> AddFormAsync(string nameEs, string nameEn, CancellationToken ct, bool? reading = true) =>
        FormAsync(Factory, Host, Publisher, nameEs, nameEn, reading, ct);

    /// <summary>A newer version of the form sealed with other names, as a publication leaves it before it is activated; returns its Id.</summary>
    public Task<Guid> SealVersionAsync(FormReceivedDocumentScenarioForm form, string nameEs, string nameEn, CancellationToken ct) => DbAsync(async db =>
    {
        var active = await db.Set<BusinessFormVersion>().AsNoTracking().SingleAsync(value => value.Id == form.VersionId, ct);
        var number = await db.Set<BusinessFormVersion>().Where(value => value.DefinitionId == form.DefinitionId).MaxAsync(value => value.Number, ct) + 1;
        var version = BusinessFormVersion.Create(form.DefinitionId, number, "documents-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        version.SealSchema(active.SchemaJson);
        version.SealPresentation(Names(form.Prefix + " " + nameEs, form.Prefix + " " + nameEn), active.SubmitLabelJson!, "es", active.QuestionCount!.Value);
        if (active.DocumentReadingJson is { } reading) version.SealDocumentReading(reading);
        db.Set<BusinessFormVersion>().Add(version);
        await db.SaveChangesAsync(ct);
        return version.Id;
    });

    /// <summary>The sealed version becomes the form's active one, published.</summary>
    public Task ActivateVersionAsync(Guid definitionId, Guid versionId, CancellationToken ct) => DbAsync(async db =>
    {
        var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == definitionId, ct);
        var version = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == versionId, ct);
        db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(Factory, definition, version, Publisher, ["es", "en"]));
        Assert.True(definition.ActivateVersion(definition.ActiveVersionId, versionId).IsSuccess);
        await db.SaveChangesAsync(ct);
        return true;
    });

    /// <summary>The form is deleted, as «Eliminar» leaves it: the documents in the inbox keep it.</summary>
    public Task DeleteFormAsync(Guid definitionId, CancellationToken ct) => DbAsync(async db =>
    {
        db.Set<BusinessFormDefinition>().Remove(await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == definitionId, ct));
        await db.SaveChangesAsync(ct);
        return true;
    });

    /// <summary>
    /// The engine's authentic proposal: propose-entry with the read it came from, the candidates read of the person who sent it or, for
    /// an extraction, the form read of the reviewer.
    /// </summary>
    public static AssistantObservedProposal Proposal(IReadOnlyList<FormReceivedDocumentScenarioForm> forms, Guid reader, string outcome,
        Func<FormReceivedDocumentScenarioForm, IReadOnlyList<(Guid FieldId, string Type, string? Value, bool Doubtful)>> values, bool formRead = false)
    {
        var read = new FormDocumentReadPayload("complete", reader, "es", true, forms.Select(form => new FormDocumentCandidate(form.DefinitionId, form.VersionId,
            form.Revision, form.Name, formRead ? null : "Un comprobante de pago.",
            [new(form.AmountId, "decimal", "Monto", true, null, [], null), new(form.OperationId, "shortText", "Operación", false, 40, [], null)])).ToArray());
        var proposal = new FormDocumentEntryProposal("proposed", 1, outcome, outcome == FormDocumentEntryProposal.Unknown ? []
            : forms.Select(form => new FormDocumentEntryCandidate(form.DefinitionId, form.VersionId, form.Revision,
                values(form).Select(value => new FormDocumentEntryValue(value.FieldId, value.Type, value.Value, value.Doubtful)).ToArray())).ToArray(),
            "comprobante de pago");
        return Observed(read, proposal, formRead ? BusinessFormDocumentAssistantCapabilitySource.FormId : BusinessFormDocumentAssistantCapabilitySource.CandidatesId);
    }

    /// <summary>
    /// A published copy of «Comprobante de transferencia» as the library leaves it for the inbox: the standard's key and version, and its
    /// duplicate keys (operation number, amount and date) bound to its questions, with reading on.
    /// </summary>
    public async Task<FormReceivedDocumentScenarioTransfer> TransferAsync(CancellationToken ct)
    {
        var operation = FormDashboardScenario.Field("shortText", "Número de operación", "Operation number") with { MaxLength = 40 };
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount") with { Required = true };
        var date = FormDashboardScenario.Field("date", "Fecha", "Date");
        var published = await PublishAsync(Factory, Host, Publisher, "T", "Transferencias", "Transfers", [operation, amount, date], "Un comprobante de transferencia.",
            ct, definition => definition.RecordStandard("forms.transfer-receipt", 1, JsonSerializer.Serialize(new Dictionary<string, Guid>
            {
                ["operationNumber"] = operation.FieldId, ["amount"] = amount.FieldId, ["date"] = date.FieldId,
            })));
        return new(published.DefinitionId, published.VersionId, published.Revision, published.Prefix, published.Name, operation.FieldId, amount.FieldId, date.FieldId);
    }

    /// <summary>A current response of the copy with that operation, amount and date, written straight to PostgreSQL; returns its number.</summary>
    public async Task<string> RespondAsync(FormReceivedDocumentScenarioTransfer transfer, string operation, decimal amount, DateOnly date, CancellationToken ct) =>
        await DbAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == transfer.DefinitionId, ct);
            var receiptId = Guid.NewGuid();
            var now = Factory.Clock.GetUtcNow().UtcDateTime.AddDays(-1);
            var values = JsonSerializer.Serialize(new object[]
            {
                new { fieldId = transfer.OperationId, value = operation }, new { fieldId = transfer.AmountId, value = amount }, new { fieldId = transfer.DateId, value = date },
            }, JsonSerializerOptions.Web);
            var record = BusinessFormRecord.Create(transfer.DefinitionId, transfer.VersionId, definition.AllocateRecordNumber(), Sender, receiptId, now, values);
            db.Set<BusinessFormRecord>().Add(record);
            db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receiptId, transfer.DefinitionId, transfer.VersionId, "documents-test", record.Id,
                record.Number, now));
            db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, transfer.DefinitionId, transfer.OperationId, "shortText", textValue: operation));
            db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, transfer.DefinitionId, transfer.AmountId, "decimal", decimalValue: amount));
            db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, transfer.DefinitionId, transfer.DateId, "date", dateValue: date));
            await db.SaveChangesAsync(ct);
            return transfer.Prefix + "-" + record.Number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        });

    /// <summary>The sender's proposal for the copy, read with its three duplicate keys.</summary>
    public AssistantObservedProposal TransferProposal(FormReceivedDocumentScenarioTransfer transfer, string operation, string amount, string date)
    {
        var read = new FormDocumentReadPayload("complete", Sender, "es", true, [new FormDocumentCandidate(transfer.DefinitionId, transfer.VersionId, transfer.Revision,
            transfer.Name, "Un comprobante de transferencia.",
            [new(transfer.OperationId, "shortText", "Número de operación", false, 40, [], null), new(transfer.AmountId, "decimal", "Monto", true, null, [], null),
                new(transfer.DateId, "date", "Fecha", false, null, [], null)])]);
        var proposal = new FormDocumentEntryProposal("proposed", 1, FormDocumentEntryProposal.Sure,
            [new(transfer.DefinitionId, transfer.VersionId, transfer.Revision,
                [new(transfer.OperationId, "shortText", operation, false), new(transfer.AmountId, "decimal", amount, false), new(transfer.DateId, "date", date, false)])],
            "comprobante de transferencia");
        return Observed(read, proposal, BusinessFormDocumentAssistantCapabilitySource.CandidatesId);
    }

    /// <summary>
    /// A published form «Cobros» with reading on: an amount, the kind of payment as a choice of its own («Transferencia» or «Depósito»),
    /// the bank from a Manual catalog of its own, with Galicia and Nación active and «Banco viejo» deactivated, and the branch from a
    /// catalog chosen first by its province, where «Centro» is in Córdoba and in Mendoza.
    /// </summary>
    public async Task<FormReceivedDocumentScenarioChoices> ChoicesAsync(CancellationToken ct)
    {
        var names = Texts("Bancos " + Guid.NewGuid().ToString("N"), "Banks");
        var catalog = BusinessFormCatalog.CreateManual("es", names, names);
        var galicia = catalog.AddManualItem("GAL", "Galicia", "Galicia", null, null, 1).Value;
        var nacion = catalog.AddManualItem("NAC", "Nación", "Nación", null, null, 2).Value;
        var old = catalog.AddManualItem("OLD", "Banco viejo", "Old bank", null, null, 3).Value;
        Assert.True(catalog.SetManualItemActive(old, false).IsSuccess);
        await InTransactionAsync(async services =>
        {
            var repository = services.GetRequiredService<IFormCatalogRepository>();
            repository.Add(catalog);
            repository.AddItems([galicia, nacion, old]);
            await Task.CompletedTask;
        }, ct);
        var branches = await CatalogAsync("Sucursales", "Provincia", [("Centro", "Córdoba"), ("Centro", "Mendoza"), ("Norte", "Córdoba")], ct);
        var transfer = FormDashboardScenario.Choice("Transferencia", "Transfer");
        var deposit = FormDashboardScenario.Choice("Depósito", "Deposit");
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount") with { Required = true };
        var kind = FormDashboardScenario.Field("choice", "Tipo de pago", "Kind of payment", [transfer, deposit]);
        var bank = FormDashboardScenario.Field("choice", "Banco", "Bank", catalog: catalog.Id);
        var branch = FormDashboardScenario.Field("choice", "Sucursal", "Branch") with { Source = new(branches.CatalogId, GroupBy: branches.FieldId) };
        var published = await PublishAsync(Factory, Host, Publisher, "C", "Cobros", "Collections", [amount, kind, bank, branch], "Un comprobante de cobro.", ct);
        return new(published.DefinitionId, published.VersionId, published.Revision, published.Name, amount.FieldId, kind.FieldId, transfer.ChoiceId,
            deposit.ChoiceId, bank.FieldId, galicia.Id, nacion.Id, old.Id, branch.FieldId);
    }

    /// <summary>The sender's proposal for «Cobros»: the amount and the kind of payment read, the bank left to the server.</summary>
    public AssistantObservedProposal ChoicesProposal(FormReceivedDocumentScenarioChoices form, string amount, Guid kind) =>
        ChoicesProposal([form], amount, kind, FormDocumentEntryProposal.Sure);

    /// <summary>A doubt between two «Cobros» forms, read alike: the document is proposed for the first one.</summary>
    public AssistantObservedProposal ChoicesDoubtProposal(FormReceivedDocumentScenarioChoices first, FormReceivedDocumentScenarioChoices second) =>
        ChoicesProposal([first, second], "150000", null, FormDocumentEntryProposal.Doubt);

    // The sender's proposal for each «Cobros» given: the amount and the kind of payment read (each form's transfer unless one is given), the
    // bank left to the server.
    private AssistantObservedProposal ChoicesProposal(IReadOnlyList<FormReceivedDocumentScenarioChoices> forms, string amount, Guid? kind, string outcome)
    {
        var read = new FormDocumentReadPayload("complete", Sender, "es", true, forms.Select(form => new FormDocumentCandidate(form.DefinitionId, form.VersionId,
            form.Revision, form.Name, "Un comprobante de cobro.",
            [new(form.AmountId, "decimal", "Monto", true, null, [], null),
                new(form.KindId, "choice", "Tipo de pago", false, null, [new(form.TransferId, "Transferencia"), new(form.DepositId, "Depósito")], null),
                new(form.BankId, "choice", "Banco", false, null, [], "Bancos")])).ToArray());
        var proposal = new FormDocumentEntryProposal("proposed", 1, outcome, forms.Select(form => new FormDocumentEntryCandidate(form.DefinitionId, form.VersionId,
            form.Revision, [new(form.AmountId, "decimal", amount, false), new(form.KindId, "choice", (kind ?? form.TransferId).ToString("D"), false),
                new(form.BankId, "choice", null, false)])).ToArray(), "comprobante de cobro");
        return Observed(read, proposal, BusinessFormDocumentAssistantCapabilitySource.CandidatesId);
    }

    /// <summary>
    /// A Manual catalog of its own with a text field and an option for each text and value of that field, in order; returns the catalog,
    /// its field and its options.
    /// </summary>
    public async Task<FormReceivedDocumentScenarioCatalog> CatalogAsync(string name, string fieldName, IReadOnlyList<(string Text, string? Value)> items,
        CancellationToken ct)
    {
        var names = Texts(name + " " + Guid.NewGuid().ToString("N"), name);
        var catalog = BusinessFormCatalog.CreateManual("es", names, names);
        var field = new BusinessFormCatalogField(BusinessFormCatalogField.CreateIdentity(), "campo", Texts(fieldName, fieldName), "text", null, false, false,
            null, null, 120, "data", 0, true);
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var options = items.Select((item, index) =>
        {
            var option = catalog.AddManualItem("O" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), item.Text, item.Text, null, null, index + 1).Value;
            if (item.Value is not null)
                Assert.True(catalog.SetManualItemFields(option, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString("D")] = item.Value })).IsSuccess);
            return option;
        }).ToArray();
        await InTransactionAsync(async services =>
        {
            var repository = services.GetRequiredService<IFormCatalogRepository>();
            repository.Add(catalog);
            repository.AddItems(options);
            await Task.CompletedTask;
        }, ct);
        return new(catalog.Id, field.Id, options.Select(option => option.Id).ToArray());
    }

    /// <summary>
    /// A published form «Depósitos» with reading on whose branch question is filtered by the person's zone, created as an administrator
    /// does it: «Zonas», «Sucursales» with its zone field, the datum «Zona» and the sender in the center and the reviewer in the south. A
    /// scenario with a database of its own: the data of the people are limited for the whole database.
    /// </summary>
    public async Task<FormReceivedDocumentScenarioCatalogForm> BranchesAsync(CancellationToken ct)
    {
        Assert.True(Isolated);
        var manager = await PersonAsync("Administra datos", [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.PersonDataManage, Permissions.Users.Manage], ct);
        using var client = Client(manager);
        var zones = await PostAsync(client, "/api/forms/catalogs", new
        {
            name = new { es = "Zonas", en = "Zones" }, description = new { es = "Zonas", en = "Zones" },
            items = new[] { CatalogItem("central", "CENTRAL", "Centro", "Central"), CatalogItem("south", "SOUTH", "Sur", "South") },
        }, HttpStatusCode.Created, ct);
        var zonesId = zones.GetProperty("catalog").GetProperty("id").GetGuid();
        var central = zones.GetProperty("itemIdentityMap").GetProperty("central").GetGuid();
        var south = zones.GetProperty("itemIdentityMap").GetProperty("south").GetGuid();
        object ZoneField(Guid? id) => new
        {
            localId = id is null ? "zone" : null, id, key = "zone", name = new { es = "Zona", en = "Zone" }, type = "catalog", sourceCatalogId = zonesId,
            required = false, unique = false, min = (string?)null, max = (string?)null, maxLength = (int?)null, role = "filter", position = 0, isActive = true,
        };
        var created = await PostAsync(client, "/api/forms/catalogs", new
        {
            name = new { es = "Sucursales", en = "Branches" }, description = new { es = "Sucursales", en = "Branches" }, fields = new[] { ZoneField(null) },
            items = new[] { CatalogItem("center", "BC", "Sucursal Centro", "Central branch"), CatalogItem("south", "BS", "Sucursal Sur", "South branch") },
        }, HttpStatusCode.Created, ct);
        var branchesId = created.GetProperty("catalog").GetProperty("id").GetGuid();
        var zoneFieldId = created.GetProperty("fieldIdentityMap").GetProperty("zone").GetGuid();
        var centerId = created.GetProperty("itemIdentityMap").GetProperty("center").GetGuid();
        var southId = created.GetProperty("itemIdentityMap").GetProperty("south").GetGuid();
        object Branch(string localId, Guid itemId, string code, string es, string en, Guid zone) => new
        {
            localId, itemId, code, text = new { es, en }, isActive = true, fields = new Dictionary<string, string> { [zoneFieldId.ToString("D")] = zone.ToString("D") },
        };
        using (var updated = await client.PutAsJsonAsync($"/api/forms/catalogs/{branchesId}", new
        {
            expectedRevision = 1, name = new { es = "Sucursales", en = "Branches" }, description = new { es = "Sucursales", en = "Branches" },
            fields = new[] { ZoneField(zoneFieldId) },
            items = new[] { Branch("center", centerId, "BC", "Sucursal Centro", "Central branch", central), Branch("south", southId, "BS", "Sucursal Sur", "South branch", south) },
        }, ct))
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var attribute = await PostAsync(client, "/api/forms/person-attributes", new
        { key = "zone", name = new { es = "Zona", en = "Zone" }, type = "Catalog", sourceCatalogId = zonesId }, HttpStatusCode.OK, ct);
        var attributeId = attribute.GetProperty("id").GetGuid();
        foreach (var (person, zone) in new[] { (Sender, central), (Reviewer, south) })
        {
            using var data = await client.PutAsJsonAsync($"/api/forms/person-data/{person}", new
            { expectedRevision = (long?)null, values = new Dictionary<string, string> { [attributeId.ToString("D")] = zone.ToString("D") } }, ct);
            Assert.Equal(HttpStatusCode.OK, data.StatusCode);
        }
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount") with { Required = true };
        var branch = FormDashboardScenario.Field("choice", "Sucursal", "Branch") with
        {
            Source = new(branchesId, Match: [new FormCatalogMatch(zoneFieldId, PersonAttributeId: attributeId)]),
        };
        var published = await PublishAsync(Factory, Host, Publisher, "D", "Depósitos", "Deposits", [amount, branch], CatalogRecognition, ct);
        return new(published.DefinitionId, published.VersionId, published.Revision, published.Name, amount.FieldId, branch.FieldId, [centerId, southId]);
    }

    /// <summary>
    /// A published form «Compras» with reading on: an amount and the supplier, from a catalog of its own with that many options, «Proveedor 001»
    /// onwards, in order.
    /// </summary>
    public async Task<FormReceivedDocumentScenarioCatalogForm> SuppliersAsync(int count, CancellationToken ct)
    {
        var suppliers = await CatalogAsync("Proveedores", "Código", Enumerable.Range(1, count)
            .Select(index => ("Proveedor " + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), (string?)null)).ToArray(), ct);
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount") with { Required = true };
        var supplier = FormDashboardScenario.Field("choice", "Proveedor", "Supplier", catalog: suppliers.CatalogId);
        var published = await PublishAsync(Factory, Host, Publisher, "P", "Compras", "Purchases", [amount, supplier], CatalogRecognition, ct);
        return new(published.DefinitionId, published.VersionId, published.Revision, published.Name, amount.FieldId, supplier.FieldId, suppliers.ItemIds);
    }

    /// <summary>The proposal for a form with an amount and a catalog choice: the amount read, the choice left to the server.</summary>
    public AssistantObservedProposal CatalogProposal(FormReceivedDocumentScenarioCatalogForm form, Guid? sender = null)
    {
        var read = new FormDocumentReadPayload("complete", sender ?? Sender, "es", true, [new FormDocumentCandidate(form.DefinitionId, form.VersionId, form.Revision,
            form.Name, CatalogRecognition,
            [new(form.AmountId, "decimal", "Monto", true, null, [], null), new(form.ChoiceId, "choice", "Opción", false, null, [], "Opciones")])]);
        var proposal = new FormDocumentEntryProposal("proposed", 1, FormDocumentEntryProposal.Sure,
            [new(form.DefinitionId, form.VersionId, form.Revision, [new(form.AmountId, "decimal", "150000", false), new(form.ChoiceId, "choice", null, false)])],
            "comprobante");
        return Observed(read, proposal, BusinessFormDocumentAssistantCapabilitySource.CandidatesId);
    }

    private const string CatalogRecognition = "Un comprobante con un monto y una opción.";

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>A one-page PDF with a title of its own, so no two documents repeat each other unless a test means it.</summary>
    public static byte[] Pdf()
    {
        using var output = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Title = Guid.NewGuid().ToString("N") }))
        {
            using var paint = new SKPaint { Color = SKColors.Black };
            pdf.BeginPage(200, 200).DrawRect(10, 10, 40, 20, paint);
            pdf.EndPage();
            pdf.Close();
        }
        return output.ToArray();
    }

    private static AssistantObservedProposal Observed(FormDocumentReadPayload read, FormDocumentEntryProposal proposal, string capability)
    {
        var turn = Guid.CreateVersion7();
        return new(turn, Guid.CreateVersion7(), new(BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, BusinessFormDocumentAssistantCapabilitySource.Version),
            proposal) { ObservedReads = [new(turn, 1, new(capability, BusinessFormDocumentAssistantCapabilitySource.Version), read)] };
    }

    // A form like «Pagos»: with its reading on (true), off (false) or without one (null).
    private static async Task<FormReceivedDocumentScenarioForm> FormAsync(ApiFactory factory, WebApplicationFactory<Program> host, Guid publisher, string nameEs,
        string nameEn, bool? reading, CancellationToken ct)
    {
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount") with { Required = true };
        var operation = FormDashboardScenario.Field("shortText", "Operación", "Operation") with { MaxLength = 40 };
        var receipt = FormDashboardScenario.Field("document", "Comprobante", "Receipt") with { Files = new(0, 1) };
        var published = await PublishAsync(factory, host, publisher, "R", nameEs, nameEn, [amount, operation, receipt], reading is null ? null : "Un comprobante de pago.",
            ct, readingEnabled: reading ?? false);
        return new(published.DefinitionId, published.VersionId, published.Revision, published.Prefix, published.Name, amount.FieldId, operation.FieldId,
            receipt.FieldId, published.NameEn);
    }

    // A published form of its own (a unique prefix before its name in each language), with a reading when it has a recognition.
    private static async Task<(Guid DefinitionId, Guid VersionId, long Revision, string Prefix, string Name, string NameEn)> PublishAsync(ApiFactory factory,
        WebApplicationFactory<Program> host, Guid publisher, string letter, string nameEs, string nameEn, CanonicalFormField[] fields, string? recognition,
        CancellationToken ct, Action<BusinessFormDefinition>? configure = null, bool readingEnabled = true)
    {
        var prefix = letter + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var definition = BusinessFormDefinition.Create();
        var version = BusinessFormVersion.Create(definition.Id, 1, "documents");
        var names = Names(prefix + " " + nameEs, prefix + " " + nameEn);
        var submit = Names("Enviar", "Submit");
        Assert.True(definition.SetDraft(1, prefix + " " + nameEs, names, prefix, "es", submit,
            JsonSerializer.Serialize(new FormQuestionSchema(1, fields), JsonSerializerOptions.Web), fields.Length, true).IsSuccess);
        configure?.Invoke(definition);
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), JsonSerializerOptions.Web));
        version.SealPresentation(names, submit, "es", fields.Length);
        if (recognition is not null)
            version.SealDocumentReading(JsonSerializer.Serialize(new FormDocumentReading(readingEnabled, recognition, []), JsonSerializerOptions.Web));
        await Scope(host, async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, publisher, ["es", "en"]));
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(ct);
            return true;
        });
        return (definition.Id, version.Id, definition.Revision, prefix, prefix + " " + nameEs, prefix + " " + nameEn);
    }

    private static Task<Guid> PersonAsync(WebApplicationFactory<Program> host, string name, IReadOnlyCollection<string> permissions, string culture,
        CancellationToken ct) => InTransactionAsync(host, async services =>
        {
            var role = "documents-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, permissions, ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("documents")).Value, null, false, name, culture, ct);
            await users.SetRolesAsync(user.Id, [role], ct);
            return user.Id;
        }, ct);

    private static async Task<T> Scope<T>(WebApplicationFactory<Program> host, Func<IServiceProvider, Task<T>> call)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider);
    }

    // Like a use case: a new scope and a real boundary, which writing accounts and roles needs.
    private static Task<T> InTransactionAsync<T>(WebApplicationFactory<Program> host, Func<IServiceProvider, Task<T>> action, CancellationToken ct) =>
        Scope(host, async services =>
        {
            var value = default(T)!;
            var saved = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async _ =>
            {
                value = await action(services);
                return Result.Success();
            }, CommitPolicy.OnSuccess, ct);
            Assert.True(saved.IsSuccess);
            return value;
        });

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object payload, HttpStatusCode status, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(path, payload, ct);
        Assert.Equal(status, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static object CatalogItem(string localId, string code, string es, string en) => new { localId, code, text = new { es, en }, isActive = true };

    private static Dictionary<string, string> Texts(string es, string en) => new() { ["es"] = es, ["en"] = en };

    private static string Names(string es, string en) => JsonSerializer.Serialize(Texts(es, en));

    public async ValueTask DisposeAsync()
    {
        if (Isolated) await DbAsync(db => db.Database.EnsureDeletedAsync());
        await Host.DisposeAsync();
    }
}

internal sealed record FormReceivedDocumentScenarioForm(Guid DefinitionId, Guid VersionId, long Revision, string Prefix, string Name, Guid AmountId,
    Guid OperationId, Guid ReceiptId, string NameEn);

internal sealed record FormReceivedDocumentScenarioTransfer(Guid DefinitionId, Guid VersionId, long Revision, string Prefix, string Name, Guid OperationId,
    Guid AmountId, Guid DateId);

internal sealed record FormReceivedDocumentScenarioChoices(Guid DefinitionId, Guid VersionId, long Revision, string Name, Guid AmountId, Guid KindId,
    Guid TransferId, Guid DepositId, Guid BankId, Guid GaliciaId, Guid NacionId, Guid OldBankId, Guid BranchId);

internal sealed record FormReceivedDocumentScenarioCatalog(Guid CatalogId, Guid FieldId, IReadOnlyList<Guid> ItemIds);

internal sealed record FormReceivedDocumentScenarioCatalogForm(Guid DefinitionId, Guid VersionId, long Revision, string Name, Guid AmountId, Guid ChoiceId,
    IReadOnlyList<Guid> ItemIds);

internal sealed record FormReceivedDocumentSent(StructuredDocumentRegistration Reply, byte[] Content, StructuredDocumentRegistrationRequest Request)
{
    public Guid Id => Reply.DocumentId ?? throw new InvalidOperationException("The document was not registered.");
}
