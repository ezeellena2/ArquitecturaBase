using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Domain.UnitTests.Users;

public sealed class AdministrativeChangeTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Actor = Guid.CreateVersion7();
    private static readonly Guid Target = Guid.CreateVersion7();

    [Fact]
    public void A_change_keeps_who_made_it_on_what_and_when()
    {
        var change = AdministrativeChange.Create(
            AdministrativeChangeKind.RoleRenamed, Actor, Target, Now, targetName: "Ventas", before: "Venta", after: "Ventas");

        Assert.NotEqual(Guid.Empty, change.Id);
        Assert.Equal(AdministrativeChangeKind.RoleRenamed, change.Kind);
        Assert.Equal(Actor, change.ActorUserId);
        Assert.Equal(Target, change.TargetId);
        Assert.Equal("Ventas", change.TargetName);
        Assert.Equal("Venta", change.Before);
        Assert.Equal("Ventas", change.After);
        Assert.Equal(Now, change.OccurredAtUtc);
        Assert.Empty(change.Added);
        Assert.Empty(change.Removed);
    }

    [Fact]
    public void The_seed_has_no_actor()
    {
        var change = AdministrativeChange.Create(AdministrativeChangeKind.RoleCreated, actorUserId: null, Target, Now);

        Assert.Null(change.ActorUserId);
    }

    [Fact]
    public void What_was_added_and_removed_is_kept_once_and_in_order()
    {
        var change = AdministrativeChange.Create(
            AdministrativeChangeKind.RolePermissionsChanged, Actor, Target, Now,
            added: ["users.manage", "roles.read", "users.manage"], removed: ["settings.manage"]);

        Assert.Equal(["roles.read", "users.manage"], change.Added);
        Assert.Equal(["settings.manage"], change.Removed);
    }

    [Fact]
    public void A_change_needs_a_known_kind_a_target_and_a_real_actor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AdministrativeChange.Create((AdministrativeChangeKind)999, Actor, Target, Now));
        Assert.Throws<ArgumentException>(() =>
            AdministrativeChange.Create(AdministrativeChangeKind.UserDeleted, Actor, Guid.Empty, Now));
        Assert.Throws<ArgumentException>(() =>
            AdministrativeChange.Create(AdministrativeChangeKind.UserDeleted, Guid.Empty, Target, Now));
    }

    [Fact]
    public void A_state_or_a_name_longer_than_its_column_is_a_bug()
    {
        var tooLong = new string('a', AdministrativeChange.TextMaxLength + 1);

        Assert.Throws<ArgumentException>(() =>
            AdministrativeChange.Create(AdministrativeChangeKind.RoleRenamed, Actor, Target, Now, before: tooLong));
        Assert.Throws<ArgumentException>(() =>
            AdministrativeChange.Create(AdministrativeChangeKind.RoleRenamed, Actor, Target, Now, targetName: tooLong));
    }

    [Theory]
    [InlineData(false, false, "None")]
    [InlineData(true, false, "Unverified")]
    [InlineData(true, true, "Verified")]
    public void A_contact_is_kept_only_by_its_state(bool present, bool verified, string expected) =>
        Assert.Equal(expected, AdministrativeChange.ContactState(present, verified));

    [Theory]
    [InlineData(true, "Active")]
    [InlineData(false, "Inactive")]
    public void An_account_status_is_active_or_inactive(bool isActive, string expected) =>
        Assert.Equal(expected, AdministrativeChange.Status(isActive));
}
