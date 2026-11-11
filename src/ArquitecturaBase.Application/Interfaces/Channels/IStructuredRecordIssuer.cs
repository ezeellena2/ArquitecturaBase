using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// An optional issuer of a document with legal value for a response, such as a voucher a tax agency authorizes. Forms offers its
/// «Issue» rule action only on forms copied from <see cref="StandardKey"/> and hands it the bound data of the response. Issuing is
/// durable and idempotent by run: enqueueing again returns the same issuance, the issuer works outside the forms transaction and
/// the run asks again until it ends. An optional collection: without issuers the action is not offered.
/// </summary>
public interface IStructuredRecordIssuer
{
    /// <summary>A stable lowercase key («arca.invoice»), also the source of its result.</summary>
    string IssuerId { get; }

    /// <summary>Its name by language (es, en), as the rule editor shows it.</summary>
    IReadOnlyDictionary<string, string> Name { get; }

    /// <summary>The key of the library standard whose bound data it reads.</summary>
    string StandardKey { get; }

    /// <summary>The permission a person needs to decide on an issuance in review, besides managing the rules.</summary>
    string ReviewPermission { get; }

    /// <summary>The data its result brings, which the messages after the action may use.</summary>
    IReadOnlyList<StructuredRecordResultField> ResultFields { get; }

    /// <summary>
    /// Where a copy of the issued document goes by email, read from the bound data of its response: the address of its receiver when
    /// the data ask for the copy and have one; null otherwise, and the copy does not apply.
    /// </summary>
    string? CopyRecipient(IReadOnlyList<StructuredIssueDatum> data);

    /// <summary>
    /// What would come out of these bound data now («¿La emito?»), without saving anything. Forms asks it again with each change, under
    /// its read quota: a registry it asks (a party written by its tax id) is limited and cached by the module.
    /// </summary>
    Task<StructuredIssuePreview> PreviewAsync(IReadOnlyList<StructuredIssueDatum> data, CancellationToken cancellationToken);

    /// <summary>Queues the issuance of a run once; a request it cannot take comes back rejected, with nothing queued.</summary>
    Task<StructuredIssueResponse> EnqueueAsync(StructuredIssueRequest request, CancellationToken cancellationToken);

    /// <summary>Where the issuance of a run stands; <see cref="StructuredIssueState.NotFound"/> if it was never queued.</summary>
    Task<StructuredIssueResponse> GetAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Applies a person's decision on an issuance in review and returns where it stands.</summary>
    Task<StructuredIssueResponse> ReviewAsync(Guid runId, StructuredIssueReview review, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels an issuance while the provider cannot have taken it yet (in a voucher, before it has a number); otherwise it comes
    /// back as it stands, and whoever voids waits for it to end.
    /// </summary>
    Task<StructuredIssueResponse> CancelAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Queues once, by its own run, the reversal of an issued document for its whole amount (a credit note).</summary>
    Task<StructuredIssueResponse> EnqueueReversalAsync(Guid reversalRunId, Guid issuedRunId, CancellationToken cancellationToken);
}
