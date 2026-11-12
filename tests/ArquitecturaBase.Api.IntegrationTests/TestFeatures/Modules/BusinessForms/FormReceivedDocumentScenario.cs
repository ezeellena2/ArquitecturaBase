using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// Task 7 with PostgreSQL: a host whose private store is in memory and whose engine is scripted; two published forms of its own, «Pagos»
// with reading on and «Gastos» without it (each with an amount, an operation number and the receipt as a document question); the person
// who sends documents, a reviewer (who also reads responses) and someone who only sees the inbox; the documents arrive through the
// channel port, as a channel registers them.
internal sealed class FormReceivedDocumentScenario : IAsyncDisposable
{
    public const string Channel = "whatsapp";
    public required ApiFactory Factory { get; init; }
    public required WebApplicationFactory<Program> Host { get; init; }
    public required AssistantCopyFiles Store { get; init; }
    public required ScriptedDocumentOperations Engine { get; init; }
    public required FormReceivedDocumentScenarioForm Form { get; init; }
    public required FormReceivedDocumentScenarioForm Other { get; init; }
    public required Guid Sender { get; init; }
    public required Guid Reviewer { get; init; }
    public required Guid Reader { get; init; }

    public static async Task<FormReceivedDocumentScenario> CreateAsync(ApiFactory factory, CancellationToken ct)
    {
        var store = new AssistantCopyFiles();
        var engine = new ScriptedDocumentOperations();
        var publisher = await FormDashboardScenario.PersonAsync(factory, "Publica Documentos", [BusinessFormsPermissions.Publish], ct);
        return new FormReceivedDocumentScenario
        {
            Factory = factory,
            Store = store,
            Engine = engine,
            Host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPrivateFileStore>();
                services.AddSingleton<IPrivateFileStore>(store);
                services.RemoveAll<IAssistantOperations>();
                services.AddSingleton<IAssistantOperations>(engine);
            })),
            Form = await FormAsync(factory, publisher, "Pagos", true, ct),
            Other = await FormAsync(factory, publisher, "Gastos", false, ct),
            Sender = await FormDashboardScenario.PersonAsync(factory, "Juan Remitente", [BusinessFormsPermissions.RecordsCreate], ct),
            Reviewer = await FormDashboardScenario.PersonAsync(factory, "Ana Revisora",
                [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview, BusinessFormsPermissions.RecordsRead], ct),
            Reader = await FormDashboardScenario.PersonAsync(factory, "Luis Lector", [BusinessFormsPermissions.DocumentsRead], ct),
        };
    }

    public HttpClient Client(Guid person, string culture = "es")
    {
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, person.ToString());
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        return client;
    }

    /// <summary>
    /// A document the sender sent (a PDF of its own unless one is given), with its copy in the channel's store: read, with the engine's
    /// proposal (by default «sure» for «Pagos» with the amount and, doubtful, the operation; a null amount was not read), or not read for
    /// the reason given.
    /// </summary>
    public async Task<FormReceivedDocumentSent> SendAsync(CancellationToken ct, string? sourceId = null, string? amount = "150000",
        string operation = "778899", StructuredDocumentNotRead? notRead = null, DateTime? replyExpiresAtUtc = null, string outcome = "sure",
        byte[]? content = null, StructuredDocumentReadability readability = StructuredDocumentReadability.Readable, Guid? sender = null,
        IReadOnlyList<FormReceivedDocumentScenarioForm>? candidates = null, AssistantObservedProposal? proposal = null)
    {
        sourceId ??= "wamid." + Guid.NewGuid().ToString("N");
        content ??= Pdf();
        var from = sender ?? Sender;
        var key = "channel/documents/" + Guid.NewGuid().ToString("N") + ".pdf";
        Store.Put(key, content);
        var forms = candidates ?? [Form];
        var request = new StructuredDocumentRegistrationRequest(Channel, sourceId, from, "es", Factory.Clock.GetUtcNow().UtcDateTime,
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

    /// <summary>The port, in a scope of the host, as a channel calls it.</summary>
    public async Task<T> PortAsync<T>(Func<IStructuredDocumentChannel, Task<T>> call)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider.GetRequiredService<IStructuredDocumentChannel>());
    }

    public async Task<T> ScopeAsync<T>(Func<IServiceProvider, Task<T>> call)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        return await call(scope.ServiceProvider);
    }

    public Task SetActiveAsync(Guid person, bool active, CancellationToken ct) =>
        Factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().SetActiveAsync(person, active, ct));

    /// <summary>
    /// The engine's authentic proposal: propose-entry with the read it came from, the candidates read of the person who sent it or, for
    /// an extraction, the form read of the reviewer.
    /// </summary>
    public static AssistantObservedProposal Proposal(IReadOnlyList<FormReceivedDocumentScenarioForm> forms, Guid reader, string outcome,
        Func<FormReceivedDocumentScenarioForm, IReadOnlyList<(Guid FieldId, string Type, string? Value, bool Doubtful)>> values, bool formRead = false)
    {
        var turn = Guid.CreateVersion7();
        var read = new FormDocumentReadPayload("complete", reader, "es", true, forms.Select(form => new FormDocumentCandidate(form.DefinitionId, form.VersionId,
            form.Revision, form.Name, formRead ? null : "Un comprobante de pago.",
            [new(form.AmountId, "decimal", "Monto", true, null, [], null), new(form.OperationId, "shortText", "Operación", false, 40, [], null)])).ToArray());
        var proposal = new FormDocumentEntryProposal("proposed", 1, outcome, outcome == FormDocumentEntryProposal.Unknown ? []
            : forms.Select(form => new FormDocumentEntryCandidate(form.DefinitionId, form.VersionId, form.Revision,
                values(form).Select(value => new FormDocumentEntryValue(value.FieldId, value.Type, value.Value, value.Doubtful)).ToArray())).ToArray(),
            "comprobante de pago");
        var capability = formRead ? BusinessFormDocumentAssistantCapabilitySource.FormId : BusinessFormDocumentAssistantCapabilitySource.CandidatesId;
        return new(turn, Guid.CreateVersion7(), new(BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, BusinessFormDocumentAssistantCapabilitySource.Version),
            proposal) { ObservedReads = [new(turn, 1, new(capability, BusinessFormDocumentAssistantCapabilitySource.Version), read)] };
    }

    /// <summary>
    /// A published copy of «Comprobante de transferencia» as the library leaves it for the inbox: the standard's key and version, and its
    /// duplicate keys (operation number, amount and date) bound to its questions, with reading on.
    /// </summary>
    public async Task<FormReceivedDocumentScenarioTransfer> TransferAsync(CancellationToken ct)
    {
        var publisher = await FormDashboardScenario.PersonAsync(Factory, "Publica Transferencias", [BusinessFormsPermissions.Publish], ct);
        var operation = FormDashboardScenario.Field("shortText", "Número de operación", "Operation number") with { MaxLength = 40 };
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount") with { Required = true };
        var date = FormDashboardScenario.Field("date", "Fecha", "Date");
        var prefix = "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var definition = BusinessFormDefinition.Create();
        var version = BusinessFormVersion.Create(definition.Id, 1, "transfer");
        CanonicalFormField[] fields = [operation, amount, date];
        var names = JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = prefix + " Transferencias", ["en"] = prefix + " Transfers" });
        var submit = JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" });
        Assert.True(definition.SetDraft(1, prefix + " Transferencias", names, prefix, "es", submit,
            JsonSerializer.Serialize(new FormQuestionSchema(1, fields), JsonSerializerOptions.Web), fields.Length, true).IsSuccess);
        definition.RecordStandard("forms.transfer-receipt", 1, JsonSerializer.Serialize(new Dictionary<string, Guid>
        {
            ["operationNumber"] = operation.FieldId, ["amount"] = amount.FieldId, ["date"] = date.FieldId,
        }));
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), JsonSerializerOptions.Web));
        version.SealPresentation(names, submit, "es", fields.Length);
        version.SealDocumentReading(JsonSerializer.Serialize(new FormDocumentReading(true, "Un comprobante de transferencia.", []), JsonSerializerOptions.Web));
        await Factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(Factory, definition, version, publisher, ["es", "en"]));
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(ct);
        });
        return new(definition.Id, version.Id, definition.Revision, prefix, prefix + " Transferencias", operation.FieldId, amount.FieldId, date.FieldId);
    }

    /// <summary>A current response of the copy with that operation, amount and date, written straight to PostgreSQL; returns its number.</summary>
    public async Task<string> RespondAsync(FormReceivedDocumentScenarioTransfer transfer, string operation, decimal amount, DateOnly date, CancellationToken ct) =>
        await Factory.ExecuteDbContextAsync(async db =>
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
        var turn = Guid.CreateVersion7();
        var read = new FormDocumentReadPayload("complete", Sender, "es", true, [new FormDocumentCandidate(transfer.DefinitionId, transfer.VersionId, transfer.Revision,
            transfer.Name, "Un comprobante de transferencia.",
            [new(transfer.OperationId, "shortText", "Número de operación", false, 40, [], null), new(transfer.AmountId, "decimal", "Monto", true, null, [], null),
                new(transfer.DateId, "date", "Fecha", false, null, [], null)])]);
        var proposal = new FormDocumentEntryProposal("proposed", 1, FormDocumentEntryProposal.Sure,
            [new(transfer.DefinitionId, transfer.VersionId, transfer.Revision,
                [new(transfer.OperationId, "shortText", operation, false), new(transfer.AmountId, "decimal", amount, false), new(transfer.DateId, "date", date, false)])],
            "comprobante de transferencia");
        return new(turn, Guid.CreateVersion7(), new(BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId, BusinessFormDocumentAssistantCapabilitySource.Version),
            proposal) { ObservedReads = [new(turn, 1, new(BusinessFormDocumentAssistantCapabilitySource.CandidatesId, BusinessFormDocumentAssistantCapabilitySource.Version), read)] };
    }

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

    private static async Task<FormReceivedDocumentScenarioForm> FormAsync(ApiFactory factory, Guid publisher, string name, bool reading, CancellationToken ct)
    {
        var amount = FormDashboardScenario.Field("decimal", "Monto", "Amount") with { Required = true };
        var operation = FormDashboardScenario.Field("shortText", "Operación", "Operation") with { MaxLength = 40 };
        var receipt = FormDashboardScenario.Field("document", "Comprobante", "Receipt") with { Files = new(0, 1) };
        var prefix = "R" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var definition = BusinessFormDefinition.Create();
        var version = BusinessFormVersion.Create(definition.Id, 1, "documents");
        CanonicalFormField[] fields = [amount, operation, receipt];
        var names = JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = prefix + " " + name, ["en"] = prefix + " " + name });
        var submit = JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" });
        Assert.True(definition.SetDraft(1, prefix + " " + name, names, prefix, "es", submit,
            JsonSerializer.Serialize(new FormQuestionSchema(1, fields), JsonSerializerOptions.Web), fields.Length, true).IsSuccess);
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), JsonSerializerOptions.Web));
        version.SealPresentation(names, submit, "es", fields.Length);
        if (reading) version.SealDocumentReading(JsonSerializer.Serialize(new FormDocumentReading(true, "Un comprobante de pago.", []), JsonSerializerOptions.Web));
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
        return new(definition.Id, version.Id, definition.Revision, prefix, prefix + " " + name, amount.FieldId, operation.FieldId, receipt.FieldId);
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

internal sealed record FormReceivedDocumentScenarioForm(Guid DefinitionId, Guid VersionId, long Revision, string Prefix, string Name, Guid AmountId,
    Guid OperationId, Guid ReceiptId);

internal sealed record FormReceivedDocumentScenarioTransfer(Guid DefinitionId, Guid VersionId, long Revision, string Prefix, string Name, Guid OperationId,
    Guid AmountId, Guid DateId);

internal sealed record FormReceivedDocumentSent(StructuredDocumentRegistration Reply, byte[] Content, StructuredDocumentRegistrationRequest Request)
{
    public Guid Id => Reply.DocumentId ?? throw new InvalidOperationException("The document was not registered.");
}
