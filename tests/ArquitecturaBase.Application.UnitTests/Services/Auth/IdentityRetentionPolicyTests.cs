using System.ComponentModel.DataAnnotations;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Delivery;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

public sealed class IdentityRetentionPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_defaults_are_the_terms_decided_on_2026_11_14()
    {
        var options = new IdentityRetentionOptions();

        Assert.Equal(30, options.LoginCodeDays);
        Assert.Equal(12, options.LoginAuditMonths);
        Assert.Equal(30, options.InvitationDays);
        Assert.Equal(14, options.SessionDaysAfterExpiry);
        Assert.Equal(500, options.BatchSize);
        Assert.Equal(24, options.IntervalHours);
        Assert.True(options.RunInBackground);
        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Codes_and_links_go_after_thirty_days()
    {
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, new IdentityRetentionOptions());

        Assert.Equal(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc), cutoffs.LoginCodesBeforeUtc);
    }

    [Fact]
    public void Sign_ins_lose_the_ip_and_the_browser_after_twelve_calendar_months()
    {
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, new IdentityRetentionOptions());

        Assert.Equal(new DateTime(2025, 11, 16, 12, 0, 0, DateTimeKind.Utc), cutoffs.LoginAuditsBeforeUtc);
    }

    [Fact]
    public void Older_invitations_go_after_thirty_days()
    {
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, new IdentityRetentionOptions { InvitationDays = 45 });

        Assert.Equal(new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc), cutoffs.InvitationsBeforeUtc);
    }

    [Fact]
    public void Sessions_are_pruned_fourteen_days_after_they_expire()
    {
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, new IdentityRetentionOptions());

        Assert.Equal(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), cutoffs.SessionsExpiredBeforeUtc);
    }

    [Fact]
    public void The_terms_always_cover_the_daily_limits()
    {
        // Los límites que leen los códigos y las invitaciones miran 24 horas: ningún plazo admitido borra algo de ellas.
        var shortest = new IdentityRetentionOptions { LoginCodeDays = 1, InvitationDays = 7 };
        var cutoffs = IdentityRetentionPolicy.CutoffsAt(Now, shortest);

        Assert.Empty(Validate(shortest));
        Assert.True(cutoffs.LoginCodesBeforeUtc <= Now.AddHours(-24));
        Assert.True(cutoffs.InvitationsBeforeUtc <= Now.AddHours(-24));
        Assert.NotEmpty(Validate(new IdentityRetentionOptions { LoginCodeDays = 0 }));
    }

    [Fact]
    public void Invitations_are_kept_at_least_a_week_so_a_delivery_in_progress_is_never_left_without_its_invitation()
    {
        // La entrega de una invitación vive un día (QueuedDelivery.DefaultLifetime); el plazo mínimo queda lejos de eso.
        Assert.Empty(Validate(new IdentityRetentionOptions { InvitationDays = 7 }));
        Assert.Contains(Validate(new IdentityRetentionOptions { InvitationDays = 6 }),
            result => result.MemberNames.Contains(nameof(IdentityRetentionOptions.InvitationDays)));
        Assert.True(TimeSpan.FromDays(7) > QueuedDelivery.DefaultLifetime);
    }

    [Theory]
    [InlineData(nameof(IdentityRetentionOptions.LoginAuditMonths))]
    [InlineData(nameof(IdentityRetentionOptions.SessionDaysAfterExpiry))]
    [InlineData(nameof(IdentityRetentionOptions.BatchSize))]
    [InlineData(nameof(IdentityRetentionOptions.IntervalHours))]
    public void A_zero_is_rejected_at_startup(string property)
    {
        var options = property switch
        {
            nameof(IdentityRetentionOptions.LoginAuditMonths) => new IdentityRetentionOptions { LoginAuditMonths = 0 },
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
        Assert.Throws<ArgumentException>(() =>
            IdentityRetentionPolicy.CutoffsAt(DateTime.SpecifyKind(Now, DateTimeKind.Local), new IdentityRetentionOptions()));
    }

    private static List<ValidationResult> Validate(IdentityRetentionOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
