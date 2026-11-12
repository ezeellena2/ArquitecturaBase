using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// IA, Stage 2: the automatic assistant takes a stored spreadsheet with the request of its source (the reader pairs them): the operation
// carries it, the reference names it so a recovery makes the same request, a spreadsheet alone is only the answer a conversation waits
// for, and an interpretation in progress is replaced when its spreadsheet arrives in either order.
public sealed class WhatsAppAssistantSpreadsheetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WhatsAppAssistantDispatchService Service(WhatsAppAssistantSuggestionFixture host) => new(host.Context.Policy,
        new(host.Reader, host.Assistant, host.Forms, host.Context.Clock, host.Presentation), host.Reader, host.Requests, host.Conversation, host.UnitOfWork,
        WhatsAppAssistantAcceptanceFixture.CreateAutomatic(host), NullLogger<WhatsAppAssistantDispatchService>.Instance);

    private static async Task<WhatsAppAssistantSuggestionFixture> HostAsync()
    {
        var host = await WhatsAppAssistantSuggestionFixture.ReadyAsync();
        host.Requests.Rows.Clear();
        host.Reader.Reference = null;
        host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        return host;
    }

    private static DateTime Now(WhatsAppAssistantSuggestionFixture host) => host.Context.Clock.GetUtcNow().UtcDateTime;

    // A message of the conversation, kept so the dispatcher can mark it processed.
    private static WhatsAppMessage Message(WhatsAppAssistantSuggestionFixture host, WhatsAppMessageKind kind, string? body)
    {
        var message = WhatsAppMessage.Inbound(host.Context.Contact.Id, "wamid." + Guid.NewGuid().ToString("N"), kind, body, null, Now(host));
        host.Messages.Messages.Add(message);
        return message;
    }

    private static WhatsAppAssistantSpreadsheetRow Sheet(Guid messageId, DateTime expiresAtUtc) => new(messageId, $"whatsapp/documents/{messageId:N}.csv",
        "clientes.csv", 27, new string('c', 64), expiresAtUtc, JsonSerializer.Serialize(new StructuredSpreadsheet("clientes.csv", StructuredSpreadsheetKind.Csv,
            "text/csv", 27, new string('c', 64), "", 3, 2, [new(1, new Dictionary<string, string> { ["A"] = "Código", ["B"] = "Texto" })]), JsonSerializerOptions.Web));

    private static WhatsAppAssistantSourceRow Source(WhatsAppMessage message, string body, WhatsAppAssistantSourceState state = WhatsAppAssistantSourceState.Ready,
        WhatsAppAssistantSpreadsheetRow? spreadsheet = null) =>
        new(message.Id, message.ContactId!.Value, body, message.OccurredAtUtc, message.OccurredAtUtc.AddDays(30))
        { WaMessageId = message.WaMessageId, Kind = message.Kind, State = state, Spreadsheet = spreadsheet };

    [Fact]
    public async Task A_spreadsheet_with_its_request_goes_with_the_interpretation_and_a_recovery_makes_the_same_request()
    {
        var host = await HostAsync();
        var spreadsheet = Message(host, WhatsAppMessageKind.Spreadsheet, "Armame un catálogo con esta planilla");
        var sheet = Sheet(spreadsheet.Id, Now(host).AddDays(20));
        host.Reader.Source = Source(spreadsheet, "Armame un catálogo con esta planilla", spreadsheet: sheet) with { ContentExpiresAtUtc = sheet.ExpiresAtUtc };

        var work = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;

        var operation = work.Operation!;
        Assert.Equal(("Armame un catálogo con esta planilla", sheet.ExpiresAtUtc), (operation.Text, operation.ContentExpiresAtUtc));
        var attachment = operation.Attachment!;
        Assert.Equal((sheet.StorageKey, "clientes.csv", "text/csv", 27L, new string('c', 64), sheet.ExpiresAtUtc),
            (attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size, attachment.Sha256, attachment.ExpiresAtUtc));
        Assert.Equal((3, "Código"), (attachment.Spreadsheet!.RowCount, attachment.Spreadsheet.Rows[0].Cells["A"]));
        var row = Assert.Single(host.Requests.Rows);
        Assert.Equal((spreadsheet.Id, (Guid?)spreadsheet.Id), (row.SourceMessageId, row.AttachmentMessageId));

        var recovered = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;
        Assert.Equal((work.RequestId, operation.Text, attachment.StorageKey), (recovered.RequestId, recovered.Operation!.Text, recovered.Operation.Attachment!.StorageKey));
        Assert.Single(host.Requests.Rows);
    }

    [Fact]
    public async Task A_text_carries_the_contiguous_spreadsheet_it_pairs_with()
    {
        var host = await HostAsync();
        var previous = Guid.NewGuid();
        var text = Message(host, WhatsAppMessageKind.Text, "Armame un catálogo de clientes");
        host.Reader.Source = Source(text, "Armame un catálogo de clientes", spreadsheet: Sheet(previous, Now(host).AddDays(20)));

        var work = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;

        Assert.Equal(($"whatsapp/documents/{previous:N}.csv", "Armame un catálogo de clientes"), (work.Operation!.Attachment!.StorageKey, work.Operation.Text));
        Assert.Equal((text.Id, (Guid?)previous), (Assert.Single(host.Requests.Rows).SourceMessageId, host.Requests.Rows[0].AttachmentMessageId));
    }

    [Fact]
    public async Task A_spreadsheet_alone_stays_in_the_chat_unless_it_answers_a_change_or_a_question()
    {
        var host = await HostAsync();
        var alone = Message(host, WhatsAppMessageKind.Spreadsheet, null);
        host.Reader.Source = Source(alone, "", WhatsAppAssistantSourceState.Alone, Sheet(alone.Id, Now(host).AddDays(20)));

        Assert.Null((await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value);
        Assert.Empty(host.Requests.Rows);
        Assert.NotNull(alone.AssistantProcessedAtUtc);

        var answer = Message(host, WhatsAppMessageKind.Spreadsheet, null);
        var change = Guid.NewGuid();
        host.Reader.ChangeRequest = change;
        host.Reader.Source = Source(answer, "", WhatsAppAssistantSourceState.Alone, Sheet(answer.Id, Now(host).AddDays(20)));

        var work = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;

        Assert.Equal(("", (Guid?)change, $"whatsapp/documents/{answer.Id:N}.csv"), (work.Operation!.Text, work.Operation.PreviousRequestId, work.Operation.Attachment!.StorageKey));
        var row = Assert.Single(host.Requests.Rows);
        Assert.Equal(((Guid?)change, (Guid?)answer.Id), (row.PreviousRequestId, row.AttachmentMessageId));
        Assert.Null(answer.AssistantProcessedAtUtc);
    }

    [Fact]
    public async Task A_text_waits_for_its_spreadsheet_and_is_not_interpreted_when_the_rejection_answered()
    {
        var host = await HostAsync();
        var waiting = Message(host, WhatsAppMessageKind.Text, "Armame un catálogo con esta planilla");
        host.Reader.Source = Source(waiting, waiting.Body!, WhatsAppAssistantSourceState.Waiting);

        Assert.Null((await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value);
        Assert.Empty(host.Requests.Rows);
        Assert.Null(waiting.AssistantProcessedAtUtc);

        host.Reader.Source = Source(waiting, waiting.Body!, WhatsAppAssistantSourceState.Answered);
        Assert.Null((await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value);
        Assert.Empty(host.Requests.Rows);
        Assert.NotNull(waiting.AssistantProcessedAtUtc);
        Assert.Equal(0, host.Assistant.Submits);
    }

    // After «Cambiar algo» the text goes first and its answer is in progress when its spreadsheet arrives: the pair replaces it and
    // continues the same operation, since the replaced one never answered (the reader then finds what it continued).
    [Fact]
    public async Task An_interpretation_in_progress_is_replaced_when_its_spreadsheet_arrives_after_the_text()
    {
        var host = await HostAsync();
        var change = Guid.NewGuid();
        host.Reader.ChangeRequest = change;
        var text = Message(host, WhatsAppMessageKind.Text, "Armame un catálogo de clientes");
        host.Reader.Source = Source(text, "Armame un catálogo de clientes");
        var first = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;
        Assert.Equal((Guid?)change, first.Operation!.PreviousRequestId);
        Assert.Null(first.Operation.Attachment);

        var spreadsheet = Message(host, WhatsAppMessageKind.Spreadsheet, null);
        host.Reader.Source = Source(spreadsheet, "Armame un catálogo de clientes", spreadsheet: Sheet(spreadsheet.Id, Now(host).AddDays(20)));
        Assert.Null((await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value);
        var replaced = host.Requests.Rows.Single(row => row.RequestId == first.RequestId);
        Assert.Equal((WhatsAppAssistantRequestState.Blocked, (Guid?)change), (replaced.State, replaced.ChangeRequestId));
        Assert.Equal(first.RequestId, Assert.Single(host.Assistant.Canceled).RequestId);

        host.Reader.ChangeRequest = replaced.ChangeRequestId;
        var second = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;
        Assert.Equal(("Armame un catálogo de clientes", (Guid?)change, $"whatsapp/documents/{spreadsheet.Id:N}.csv"),
            (second.Operation!.Text, second.Operation.PreviousRequestId, second.Operation.Attachment!.StorageKey));
        Assert.Equal((Guid?)spreadsheet.Id, host.Requests.Rows.Single(row => row.RequestId == second.RequestId).AttachmentMessageId);
    }

    // The spreadsheet alone answered «Cambiar algo» and its text arrives while that answer is in progress: the pair replaces it and
    // continues the same operation.
    [Fact]
    public async Task An_answer_in_progress_is_replaced_when_its_text_arrives_after_the_spreadsheet()
    {
        var host = await HostAsync();
        var change = Guid.NewGuid();
        host.Reader.ChangeRequest = change;
        var spreadsheet = Message(host, WhatsAppMessageKind.Spreadsheet, null);
        var sheet = Sheet(spreadsheet.Id, Now(host).AddDays(20));
        host.Reader.Source = Source(spreadsheet, "", WhatsAppAssistantSourceState.Alone, sheet);
        var first = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;
        Assert.Equal(("", (Guid?)change), (first.Operation!.Text, first.Operation.PreviousRequestId));

        var text = Message(host, WhatsAppMessageKind.Text, "Agregá el campo Rubro");
        host.Reader.Source = Source(text, "Agregá el campo Rubro", spreadsheet: sheet);
        Assert.Null((await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value);
        var replaced = host.Requests.Rows.Single(row => row.RequestId == first.RequestId);
        Assert.Equal((WhatsAppAssistantRequestState.Blocked, (Guid?)change), (replaced.State, replaced.ChangeRequestId));

        host.Reader.ChangeRequest = replaced.ChangeRequestId;
        var second = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;
        Assert.Equal(("Agregá el campo Rubro", (Guid?)change, sheet.StorageKey),
            (second.Operation!.Text, second.Operation.PreviousRequestId, second.Operation.Attachment!.StorageKey));
    }

    [Fact]
    public async Task A_result_is_not_answered_when_the_latest_source_no_longer_carries_its_spreadsheet()
    {
        var host = await HostAsync();
        var change = Guid.NewGuid();
        host.Reader.ChangeRequest = change;
        var text = Message(host, WhatsAppMessageKind.Text, "Armame un catálogo de clientes");
        host.Reader.Source = Source(text, "Armame un catálogo de clientes", spreadsheet: Sheet(Guid.NewGuid(), Now(host).AddDays(20)));
        var work = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;
        host.Reader.Source = Source(text, "Armame un catálogo de clientes");
        host.Assistant.Outcome = host.Assistant.Outcome.Value with { RequestId = work.RequestId,
            Provenance = host.Assistant.Outcome.Value.Provenance! with { ChannelId = "whatsapp-assistant" } };

        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id, work.RequestId, host.Assistant.Outcome, Ct)).IsSuccess);

        var row = Assert.Single(host.Requests.Rows);
        Assert.Equal((WhatsAppAssistantRequestState.Blocked, (Guid?)change), (row.State, row.ChangeRequestId));
        Assert.Empty(host.Conversation.Outgoing);
    }

    // A continuation that failed on its own (not replaced by a newer message) leaves nothing to continue: the next message starts anew.
    [Fact]
    public async Task A_failed_continuation_leaves_nothing_for_the_next_message_to_continue()
    {
        var host = await HostAsync();
        host.Reader.ChangeRequest = Guid.NewGuid();
        var text = Message(host, WhatsAppMessageKind.Text, "Agregá el campo Rubro");
        host.Reader.Source = Source(text, "Agregá el campo Rubro");
        var work = (await Service(host).PrepareAsync(host.Context.Contact.Id, Ct)).Value!;

        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id, work.RequestId,
            ArquitecturaBase.Domain.Results.Result.Failure<ArquitecturaBase.Application.Models.Assistant.AssistantOperationResponse>(WhatsAppAssistantErrors.InvalidProposal), Ct)).IsSuccess);

        var row = Assert.Single(host.Requests.Rows);
        Assert.Equal((WhatsAppAssistantRequestState.Blocked, (Guid?)null), (row.State, row.ChangeRequestId));
    }

    [Theory]
    [InlineData(WhatsAppAssistantSourceState.Alone)]
    [InlineData(WhatsAppAssistantSourceState.Waiting)]
    [InlineData(WhatsAppAssistantSourceState.Answered)]
    public async Task A_suggestion_for_the_operator_needs_a_request_and_takes_its_spreadsheet(WhatsAppAssistantSourceState state)
    {
        var host = new WhatsAppAssistantSuggestionFixture();
        var spreadsheet = Message(host, WhatsAppMessageKind.Spreadsheet, null);
        host.Reader.Source = Source(spreadsheet, "", state, Sheet(spreadsheet.Id, Now(host).AddDays(20)));
        var request = host.Request with { SourceMessageId = spreadsheet.Id };

        Assert.Equal(WhatsAppAssistantErrors.StateConflict, (await host.Service.RequestAsync(request, Ct)).Error);

        host.Reader.Source = host.Reader.Source with { Body = "Armame un catálogo de clientes", State = WhatsAppAssistantSourceState.Ready };
        Assert.True((await host.Service.RequestAsync(request, Ct)).IsSuccess);
        Assert.Equal((Guid?)spreadsheet.Id, Assert.Single(host.Requests.Rows).AttachmentMessageId);
    }
}
