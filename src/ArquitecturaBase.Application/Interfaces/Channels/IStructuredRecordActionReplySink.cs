using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// Where a channel hands the reply to a button of an action message (<see cref="StructuredRecordActionMessage.Choices"/>): the
/// run, the person who pressed it, the id of the choice and the channel's id of the tap. The owner decides inside its own
/// transaction; the channel calls it outside any transaction of its own and does not interpret the choice. An applied reply may
/// bring a text, already in the person's language, that the channel sends back to that person once per tap. The owner keeps the
/// tap with its decision and answers that same tap the same, Applied with the same text, so a tap the channel hands over again
/// (it did not get to answer it) is answered; another tap is ignored (H21). An optional collection: without owners, replies are
/// ignored.
/// </summary>
public interface IStructuredRecordActionReplySink
{
    Task<StructuredRecordActionReply> ReplyAsync(Guid runId, Guid recipientUserId, string choiceId, Guid tapId, CancellationToken cancellationToken);
}
