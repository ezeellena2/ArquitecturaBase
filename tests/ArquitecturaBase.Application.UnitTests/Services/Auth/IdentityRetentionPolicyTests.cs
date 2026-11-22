using System.ComponentModel.DataAnnotations;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Delivery;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

public sealed class IdentityRetentionPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_installation_only_keeps_how_the_retention_runs()
    {
        var options = new IdentityRetentionOptions();

        Assert.Equal(14, options.SessionDaysAfterExpiry);
        Assert.Equal(500, options.BatchSize);
        Assert.Equal(24, options.IntervalHours);
        Assert.True(options.RunInBackground);
        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Codes_and_links_go_after_thirty_days()
    {
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, IdentityRetentionPeriods.Initial, new IdentityRetentionOptions());

        Assert.Equal(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc), cutoffs.LoginCodesBeforeUtc);
    }

    [Fact]
    public void Sign_ins_lose_the_ip_and_the_browser_after_twelve_calendar_months()
    {
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, IdentityRetentionPeriods.Initial, new IdentityRetentionOptions());

        Assert.Equal(new DateTime(2025, 11, 16, 12, 0, 0, DateTimeKind.Utc), cutoffs.LoginAuditsBeforeUtc);
    }

    // RC1-A: the periods are the ones the administrator saved in «Privacidad y retención», not the installation's.
    [Fact]
    public void Each_period_comes_from_the_saved_settings()
    {
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, new IdentityRetentionPeriods(6, 2, 45), new IdentityRetentionOptions());

        Assert.Equal(new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc), cutoffs.LoginAuditsBeforeUtc);
        Assert.Equal(new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc), cutoffs.LoginCodesBeforeUtc);
        Assert.Equal(new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), cutoffs.InvitationsBeforeUtc);
    }

    [Fact]
    public void Sessions_are_pruned_fourteen_days_after_they_expire()
    {
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, IdentityRetentionPeriods.Initial, new IdentityRetentionOptions());

        Assert.Equal(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), cutoffs.SessionsExpiredBeforeUtc);
        Assert.Equal(cutoffs.SessionsExpiredBeforeUtc, IdentityRetentionPolicy.SessionsExpiredBeforeUtc(Now, new IdentityRetentionOptions()));
    }

    [Fact]
    public void The_shortest_periods_always_cover_the_daily_limits()
    {
        // Los límites que leen los códigos y las invitaciones miran 24 horas: ningún plazo admitido borra algo de ellas.
        var shortest = new IdentityRetentionPeriods(IdentityRetentionPeriods.MinLoginAuditMonths, IdentityRetentionPeriods.MinLoginCodeDays,
            IdentityRetentionPeriods.MinInvitationDays);
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, shortest, new IdentityRetentionOptions());

        Assert.True(shortest.IsValid);
        Assert.True(cutoffs.LoginCodesBeforeUtc <= Now.AddHours(-24));
        Assert.True(cutoffs.InvitationsBeforeUtc <= Now.AddHours(-24));
        Assert.False(shortest with { LoginCodeDays = 0 } is { IsValid: true });
    }

    [Fact]
    public void Invitations_are_kept_at_least_a_week_so_a_delivery_in_progress_is_never_left_without_its_invitation()
    {
        // La entrega de una invitación vive un día (QueuedDelivery.DefaultLifetime); el plazo mínimo queda lejos de eso.
        Assert.False(IdentityRetentionPeriods.Initial with { InvitationDays = IdentityRetentionPeriods.MinInvitationDays - 1 } is { IsValid: true });
        Assert.True(TimeSpan.FromDays(IdentityRetentionPeriods.MinInvitationDays) > QueuedDelivery.DefaultLifetime);
    }

    [Theory]
    [InlineData(nameof(IdentityRetentionOptions.SessionDaysAfterExpiry))]
    [InlineData(nameof(IdentityRetentionOptions.BatchSize))]
    [InlineData(nameof(IdentityRetentionOptions.IntervalHours))]
    public void A_zero_is_rejected_at_startup(string property)
    {
        var options = property switch
        {
            nameof(IdentityRetentionOptions.SessionDaysAfterExpiry) => new IdentityRetentionOptions { SessionDaysAfterExpiry = 0 },
            nameof(IdentityRetentionOptions.BatchSize) => new IdentityRetentionOptions { BatchSize = 0 },
            _ => new IdentityRetentionOptions { IntervalHours = 0 },
        };

        Assert.Contains(Validate(options), result => result.MemberNames.Contains(property));
    }

    [Fact]
    public void A_batch_fits_in_the_lock_table_of_postgres()
    {
        // Los enlaces de un lote toman un lock por cuenta en la misma transacción.
        Assert.Empty(Validate(new IdentityRetentionOptions { BatchSize = 1000 }));
        Assert.NotEmpty(Validate(new IdentityRetentionOptions { BatchSize = 1001 }));
    }

    [Fact]
    public void A_time_that_is_not_utc_is_a_bug()
    {
        Assert.Throws<ArgumentException>(() => IdentityRetentionPolicy.CutoffsAt(DateTime.SpecifyKind(Now, DateTimeKind.Local),
            IdentityRetentionPeriods.Initial, new IdentityRetentionOptions()));
    }

    private static List<ValidationResult> Validate(IdentityRetentionOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
