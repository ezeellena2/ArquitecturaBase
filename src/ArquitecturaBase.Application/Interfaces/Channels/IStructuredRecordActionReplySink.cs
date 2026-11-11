using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// Where a channel hands the reply to a button of an action message (<see cref="StructuredRecordActionMessage.Choices"/>): the
/// run, the person who pressed it and the id of the choice. The owner decides inside its own transaction; the channel calls it
/// outside any transaction of its own and does not interpret the choice. An applied reply may bring a text, already in the
/// person's language, that the channel sends back to that person once. An optional collection: without owners, replies are
/// ignored.
/// </summary>
public interface IStructuredRecordActionReplySink
{
    Task<StructuredRecordActionReply> ReplyAsync(Guid runId, Guid recipientUserId, string choiceId, CancellationToken cancellationToken);
}
