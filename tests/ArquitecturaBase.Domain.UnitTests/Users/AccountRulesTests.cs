using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Domain.UnitTests.Users;

public sealed class AccountRulesTests
{
    private const string Emoji = "\U0001F600";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\0")]
    public void An_external_name_without_text_is_no_name(string? name)
    {
        Assert.Null(AccountRules.FitExternalDisplayName(name));
    }

    [Fact]
    public void A_short_external_name_is_kept_without_the_surrounding_spaces()
    {
        Assert.Equal("Ana Pérez", AccountRules.FitExternalDisplayName("  Ana Pérez "));
    }

    [Fact]
    public void An_external_name_of_exactly_the_limit_is_kept()
    {
        var name = new string('A', AccountRules.DisplayNameMaxLength);

        Assert.Equal(name, AccountRules.FitExternalDisplayName(name));
    }

    [Fact]
    public void A_long_external_name_is_cut_to_the_limit()
    {
        var fitted = AccountRules.FitExternalDisplayName(new string('A', 150));

        Assert.Equal(new string('A', AccountRules.DisplayNameMaxLength), fitted);
    }

    [Fact]
    public void An_emoji_split_by_the_cut_is_left_out_whole()
    {
        var fitted = AccountRules.FitExternalDisplayName(
            new string('A', AccountRules.DisplayNameMaxLength - 1) + Emoji + "B");

        Assert.Equal(new string('A', AccountRules.DisplayNameMaxLength - 1), fitted);
        Assert.True(AccountRules.IsValidDisplayName(fitted));
    }

    [Fact]
    public void The_null_character_is_removed()
    {
        Assert.Equal("Ana", AccountRules.FitExternalDisplayName("A\0na"));
    }

    [Fact]
    public void A_lone_half_of_an_emoji_is_replaced()
    {
        Assert.Equal("Ana �", AccountRules.FitExternalDisplayName("Ana " + Emoji[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ana")]
    public void A_missing_or_short_name_is_valid(string? name)
    {
        Assert.True(AccountRules.IsValidDisplayName(name));
    }

    [Fact]
    public void The_display_name_limit_is_inclusive()
    {
        Assert.True(AccountRules.IsValidDisplayName(new string('A', AccountRules.DisplayNameMaxLength)));
        Assert.False(AccountRules.IsValidDisplayName(new string('A', AccountRules.DisplayNameMaxLength + 1)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void An_account_with_an_email_or_a_phone_has_a_contact(bool hasEmail, bool hasPhone)
    {
        AccountRules.EnsureHasContact(hasEmail, hasPhone);
    }

    [Fact]
    public void An_account_without_email_or_phone_is_a_bug()
    {
        Assert.Throws<ArgumentException>(() => AccountRules.EnsureHasContact(hasEmail: false, hasPhone: false));
    }
}
