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

    /// <summary>Queues the issuance of a run once; a request it cannot take comes back rejected, with nothing queued.</summary>
    Task<StructuredIssueResponse> EnqueueAsync(StructuredIssueRequest request, CancellationToken cancellationToken);

    /// <summary>Where the issuance of a run stands; <see cref="StructuredIssueState.NotFound"/> if it was never queued.</summary>
    Task<StructuredIssueResponse> GetAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Applies a person's decision on an issuance in review and returns where it stands.</summary>
    Task<StructuredIssueResponse> ReviewAsync(Guid runId, StructuredIssueReview review, CancellationToken cancellationToken);
}
