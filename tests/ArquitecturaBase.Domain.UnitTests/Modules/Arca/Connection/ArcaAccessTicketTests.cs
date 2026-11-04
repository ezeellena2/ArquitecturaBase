using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Connection;

public sealed class ArcaAccessTicketTests
{
    private static readonly ArcaServiceCode Invoicing = ArcaServiceCode.Create("wsfe")!.Value;
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();

    [Fact]
    public void A_new_ticket_belongs_to_its_certificate_and_service_and_has_nothing_stored()
    {
        var certificateId = Guid.CreateVersion7();
        var ticket = ArcaAccessTicket.Create(certificateId, Invoicing);

        Assert.Equal(certificateId, ticket.CertificateId);
        Assert.Equal("wsfe", ticket.ServiceCode);
        Assert.Null(ticket.ProtectedCredentials);
    }

    [Fact]
    public void Without_a_ticket_or_a_lease_the_lease_is_taken()
    {
        var ticket = ArcaAccessTicket.Create(Guid.CreateVersion7(), Invoicing);

        Assert.Equal(ArcaTicketLease.Acquired, ticket.TryLease(Me, Now, Margin, Lease));
        Assert.Equal(Me, ticket.LeaseOwner);
        Assert.Equal(Now + Lease, ticket.LeaseUntilUtc);
    }

    [Fact]
    public void A_ticket_with_enough_margin_is_reused()
    {
        var ticket = Stored(expiresAtUtc: Now.AddHours(1));
        Assert.Equal(ArcaTicketLease.AlreadyValid, ticket.TryLease(Other, Now, Margin, Lease));
    }

    [Fact]
    public void A_ticket_about_to_expire_is_renewed()
    {
        var ticket = Stored(expiresAtUtc: Now.AddMinutes(10));
        Assert.Equal(ArcaTicketLease.Acquired, ticket.TryLease(Other, Now, Margin, Lease));
    }

    [Fact]
    public void Another_owner_waits_while_the_lease_lasts_and_takes_it_when_it_expires()
    {
        var ticket = ArcaAccessTicket.Create(Guid.CreateVersion7(), Invoicing);
        ticket.TryLease(Me, Now, Margin, Lease);

        Assert.Equal(ArcaTicketLease.Busy, ticket.TryLease(Other, Now.AddSeconds(30), Margin, Lease));
        Assert.Equal(ArcaTicketLease.Acquired, ticket.TryLease(Other, Now.AddSeconds(60), Margin, Lease));
        Assert.Equal(Other, ticket.LeaseOwner);
    }

    [Fact]
    public void A_blocked_ticket_is_not_requested_again_until_the_window_ends()
    {
        var ticket = ArcaAccessTicket.Create(Guid.CreateVersion7(), Invoicing);
        ticket.TryLease(Me, Now, Margin, Lease);
        Assert.True(ticket.Block(Me, Now.AddMinutes(2)));

        Assert.Null(ticket.LeaseOwner);
        Assert.Equal(ArcaTicketLease.Blocked, ticket.TryLease(Other, Now.AddMinutes(1), Margin, Lease));
        Assert.Equal(ArcaTicketLease.Acquired, ticket.TryLease(Other, Now.AddMinutes(2), Margin, Lease));
    }

    [Fact]
    public void Only_the_owner_of_the_lease_stores_blocks_or_releases()
    {
        var ticket = ArcaAccessTicket.Create(Guid.CreateVersion7(), Invoicing);
        ticket.TryLease(Me, Now, Margin, Lease);

        Assert.False(ticket.Store(Other, "CREDENTIALS", Now.AddHours(12)));
        Assert.False(ticket.Block(Other, Now.AddMinutes(2)));
        Assert.False(ticket.Release(Other));
        Assert.Null(ticket.ProtectedCredentials);
        Assert.Equal(Me, ticket.LeaseOwner);

        Assert.True(ticket.Store(Me, "CREDENTIALS", Now.AddHours(12)));
        Assert.Equal("CREDENTIALS", ticket.ProtectedCredentials);
        Assert.Equal(Now.AddHours(12), ticket.ExpiresAtUtc);
        Assert.Null(ticket.LeaseOwner);
    }

    [Fact]
    public void Releasing_lets_another_host_try_right_away()
    {
        var ticket = ArcaAccessTicket.Create(Guid.CreateVersion7(), Invoicing);
        ticket.TryLease(Me, Now, Margin, Lease);

        Assert.True(ticket.Release(Me));

        Assert.Equal(ArcaTicketLease.Acquired, ticket.TryLease(Other, Now, Margin, Lease));
    }

    [Fact]
    public void The_text_representation_never_shows_the_credentials()
    {
        Assert.DoesNotContain("CREDENTIALS", Stored(Now.AddHours(1)).ToString(), StringComparison.Ordinal);
    }

    private static ArcaAccessTicket Stored(DateTime expiresAtUtc)
    {
        var ticket = ArcaAccessTicket.Create(Guid.CreateVersion7(), Invoicing);
        ticket.TryLease(Me, Now.AddHours(-1), Margin, Lease);
        ticket.Store(Me, "CREDENTIALS", expiresAtUtc);
        return ticket;
    }
}
