using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// F40: the inbox's purge takes batches one after another, each in its own transaction, while they come out full, and stops at its time cap.
public sealed class FormReceivedDocumentRetentionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_backlog_of_expired_documents_goes_in_batches_of_a_hundred_each_in_its_own_transaction()
    {
        var f = new FormDocumentProposalFixture();
        for (var index = 0; index < 250; index++) f.Document();
        f.Inbox.Clock.Advance(TimeSpan.FromDays(FormReceivedDocument.RetentionDays + 1));

        var purged = await Service(f).PurgeExpiredAsync(Ct);

        Assert.True(purged.IsSuccess);
        Assert.Empty(f.Inbox.Documents);
        // Two full batches and a last one with the fifty left, which comes out short: the purge ends.
        Assert.Equal((3, 3), (f.UnitOfWork.Transactions, f.UnitOfWork.Commits));
    }

    [Fact]
    public async Task The_purge_stops_at_its_time_cap_and_the_next_round_goes_on()
    {
        var f = new FormDocumentProposalFixture();
        for (var index = 0; index < 250; index++) f.Document();
        f.Inbox.Clock.Advance(TimeSpan.FromDays(FormReceivedDocument.RetentionDays + 1));
        var slow = new FakeUnitOfWork { OnCommit = () => f.Inbox.Clock.Advance(FormReceivedDocumentRetentionService.MaxDuration) };

        var first = await Service(f, slow).PurgeExpiredAsync(Ct);
        var left = f.Inbox.Documents.Count;
        var second = await Service(f).PurgeExpiredAsync(Ct);

        Assert.True(first.IsSuccess && second.IsSuccess);
        Assert.Equal(150, left);
        Assert.Empty(f.Inbox.Documents);
    }

    private static FormReceivedDocumentRetentionService Service(FormDocumentProposalFixture f, FakeUnitOfWork? unitOfWork = null) =>
        new(f.Inbox, f.Inbox, unitOfWork ?? f.UnitOfWork, f.Inbox.Clock, NullLogger<FormReceivedDocumentRetentionService>.Instance);
}
