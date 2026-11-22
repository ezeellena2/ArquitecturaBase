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

    // RC1-A: «Borrar los descartados» of Configuración › Formularios, read on every batch and counted from the discard: lowering it deletes at
    // the next purge what already passed the new term, and raising it keeps the documents longer.
    [Fact]
    public async Task A_discarded_document_goes_the_configured_days_after_its_discard()
    {
        var f = new FormDocumentProposalFixture();
        f.Document();
        f.FormSettings.Settings = f.FormSettings.Settings with { DiscardedRetentionDays = 10 };
        f.Inbox.Clock.Advance(TimeSpan.FromDays(10).Subtract(TimeSpan.FromMinutes(1)));
        Assert.True((await Service(f).PurgeExpiredAsync(Ct)).IsSuccess);
        Assert.Single(f.Inbox.Documents);
        f.Inbox.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await Service(f).PurgeExpiredAsync(Ct)).IsSuccess);
        Assert.Empty(f.Inbox.Documents);

        f.Document();
        f.FormSettings.Settings = f.FormSettings.Settings with { DiscardedRetentionDays = 365 };
        f.Inbox.Clock.Advance(TimeSpan.FromDays(FormReceivedDocument.RetentionDays + 1));
        Assert.True((await Service(f).PurgeExpiredAsync(Ct)).IsSuccess);
        Assert.Single(f.Inbox.Documents);
    }

    private static FormReceivedDocumentRetentionService Service(FormDocumentProposalFixture f, FakeUnitOfWork? unitOfWork = null) =>
        new(f.Inbox, f.Inbox, f.FormSettings, unitOfWork ?? f.UnitOfWork, f.Inbox.Clock, NullLogger<FormReceivedDocumentRetentionService>.Instance);
}
