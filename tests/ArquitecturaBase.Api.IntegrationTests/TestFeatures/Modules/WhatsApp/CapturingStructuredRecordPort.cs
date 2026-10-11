using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

// A neutral fixture, never a replacement for the BusinessForms persistence tests.
internal sealed class CapturingStructuredRecordPort(StructuredFormCheckRequest snapshot) : IStructuredRecordSink
{
    private readonly StructuredFormCheckRequest _snapshot = snapshot;
    private readonly Dictionary<Guid, (Guid RecordId, IReadOnlyDictionary<string, string> Answers)> _records = [];
    internal IReadOnlyList<StructuredRecordCaptureRequest> Captures => _captures;
    internal Guid? AcceptedReceiptId { get; set; }
    internal IReadOnlyList<StructuredRecordCaptureIdentity> IgnoredCaptures => _ignoredCaptures;
    private readonly List<StructuredRecordCaptureRequest> _captures = [];
    private readonly List<StructuredRecordCaptureIdentity> _ignoredCaptures = [];

    public Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (AcceptedReceiptId is { } receiptId && request.SourceReceiptId != receiptId)
        {
            _ignoredCaptures.Add(new(request.SourceReceiptId, request.DefinitionId, request.VersionId));
            return Task.FromResult(new StructuredRecordCaptureResponse("Unavailable", null, null, null));
        }
        _captures.Add(request);
        if (request.Answers.Values.Contains("reject-fixture"))
            return Task.FromResult(new StructuredRecordCaptureResponse("Rejected", null, null, "Fixture.InvalidAnswer"));
        if (!_records.TryGetValue(request.SourceReceiptId, out var saved))
        {
            saved = (Guid.NewGuid(), request.Answers.ToDictionary());
            _records.Add(request.SourceReceiptId, saved);
        }
        return Task.FromResult(new StructuredRecordCaptureResponse("Completed", saved.RecordId, 24, null));
    }

    public Task<StructuredRecordCaptureResponse?> FindExistingReceiptAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<StructuredRecordCaptureResponse?>(null);

    internal IStructuredRecordSource Source(IPermissionService permissions) => new ReadSource(this, permissions);

    private sealed class ReadSource(CapturingStructuredRecordPort owner, IPermissionService permissions) : IStructuredRecordSource
    {
        public async Task<StructuredRecordReadResult> ReadAsync(Guid actorUserId, string culture,
            IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken)
        {
            if (!await permissions.HasPermissionAsync(actorUserId, "formrecords.read", cancellationToken))
                return new(false, new Dictionary<Guid, StructuredRecordReadReply>());
            var replies = new Dictionary<Guid, StructuredRecordReadReply>();
            foreach (var receiptId in sourceReceiptIds)
            {
                if (!owner._records.TryGetValue(receiptId, out var saved)) continue;
                var capture = owner._captures.First(item => item.SourceReceiptId == receiptId);
                var values = owner._snapshot.Questions.Select(question => new StructuredRecordReadValue(question.FieldId,
                    question.Key, question.Type, question.Label[culture],
                    saved.Answers.TryGetValue(question.Key, out var value) ? Value(question, value, culture) : null)).ToArray();
                replies.Add(receiptId, new(receiptId, saved.RecordId, "VIS-000024", capture.DefinitionId,
                    capture.VersionId, 1, owner._snapshot.Name[culture], values));
            }
            return new(true, replies);
        }

        private static object? Value(StructuredFormQuestion question, string value, string culture) =>
            string.IsNullOrEmpty(value) ? null : question.Type switch
            {
                "boolean" => value == "true",
                "choice" => new StructuredRecordChoiceValue(value,
                    question.Options!.Single(option => option.ChoiceId.ToString("D") == value).Label[culture]),
                _ => value,
            };
    }
}

internal sealed record StructuredRecordCaptureIdentity(Guid SourceReceiptId, Guid DefinitionId, Guid VersionId);

internal sealed class CapturingFormReplyTransport : IWhatsAppCloudClient
{
    internal List<WhatsAppFlowMessageResponse> Messages { get; } = [];

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Messages.Add(Assert.IsType<WhatsAppFlowMessageResponse>(message));
        return Task.FromResult(WhatsAppSendResult.Sent("wamid.reply-fixture-" + Guid.NewGuid().ToString("N")));
    }
}
