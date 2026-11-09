using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaVoucherSequenceTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();

    [Fact]
    public void A_free_sequence_is_reserved_with_a_new_fence()
    {
        var sequence = New();

        var fence = sequence.TryReserve(Me, Now, Lease);

        Assert.Equal(1, fence);
        Assert.True(sequence.Owns(Me, 1, Now.AddSeconds(119)));
        Assert.False(sequence.Owns(Me, 1, Now.AddMinutes(2)));
    }

    [Fact]
    public void A_sequence_reserved_by_another_host_is_skipped_until_its_lease_ends()
    {
        var sequence = New();
        sequence.TryReserve(Other, Now, Lease);

        Assert.Null(sequence.TryReserve(Me, Now.AddSeconds(30), Lease));

        Assert.Equal(2, sequence.TryReserve(Me, Now.AddMinutes(2), Lease));
        Assert.False(sequence.Owns(Other, 1, Now.AddMinutes(2)));
    }

    [Fact]
    public void Releasing_needs_the_same_owner_and_fence()
    {
        var sequence = New();
        var fence = sequence.TryReserve(Me, Now, Lease)!.Value;

        Assert.False(sequence.Release(Other, fence));
        Assert.True(sequence.Release(Me, fence));
        Assert.Equal(fence + 1, sequence.TryReserve(Other, Now, Lease));
    }

    [Fact]
    public void The_pending_voucher_stays_until_that_same_voucher_is_resolved()
    {
        var sequence = New();
        var voucher = Guid.CreateVersion7();

        sequence.Hold(voucher);
        sequence.ReleasePending(Guid.CreateVersion7());
        Assert.Equal(voucher, sequence.PendingVoucherId);

        sequence.ReleasePending(voucher);
        Assert.Null(sequence.PendingVoucherId);
    }

    private static ArcaVoucherSequence New() => ArcaVoucherSequence.Create(ArcaEnvironment.Testing, "27287654338", 4, 11);
}
