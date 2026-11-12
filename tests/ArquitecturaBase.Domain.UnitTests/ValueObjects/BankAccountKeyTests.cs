using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.UnitTests.ValueObjects;

// DOC1: an account to transfer to, as a receipt shows it: a CBU or a CVU with both check digits, or an alias. The keys below were
// computed by hand with the weights of each block; 2850590940090418135201 is the example of a published CBU.
public sealed class BankAccountKeyTests
{
    [Theory]
    [InlineData("2850590940090418135201", "2850590940090418135201", "285")]
    [InlineData("0070999003000041772136", "0070999003000041772136", "007")]
    [InlineData("0110 5995 4000 0001 2345 63", "0110599540000001234563", "011")]
    [InlineData(" 0170099-2-2000006779737-0 ", "0170099220000067797370", "017")]
    public void A_cbu_with_both_check_digits_says_its_bank(string input, string value, string entity)
    {
        var key = BankAccountKey.Parse(input);

        Assert.NotNull(key);
        Assert.Equal((value, BankAccountKind.Cbu, entity), (key.Value, key.Kind, key.EntityCode));
    }

    [Theory]
    [InlineData("0000003110001234567898", "0003")]
    [InlineData("0000079600000000000420", "0079")]
    public void A_cvu_starts_with_three_zeros_and_says_its_provider(string input, string entity)
    {
        var key = BankAccountKey.Parse(input);

        Assert.NotNull(key);
        Assert.Equal((input, BankAccountKind.Cvu, entity), (key.Value, key.Kind, key.EntityCode));
    }

    [Theory]
    // A wrong check digit of the first block, then of the second.
    [InlineData("2850590840090418135201")]
    [InlineData("2850590940090418135202")]
    [InlineData("0000003110001234567890")]
    [InlineData("0000003010001234567898")]
    // A digit misread in the middle of each block.
    [InlineData("2850690940090418135201")]
    [InlineData("2850590940090418136201")]
    // One digit less or more.
    [InlineData("285059094009041813520")]
    [InlineData("28505909400904181352011")]
    public void A_cbu_or_cvu_whose_digits_do_not_add_up_is_rejected(string input) => Assert.Null(BankAccountKey.Parse(input));

    [Theory]
    [InlineData("juan.perez.mp", "juan.perez.mp")]
    [InlineData(" CASA-PLAYA.2026 ", "casa-playa.2026")]
    [InlineData("abc123", "abc123")]
    [InlineData("a.b-c.d-e.f-g.h-i.jk", "a.b-c.d-e.f-g.h-i.jk")]
    public void An_alias_of_six_to_twenty_letters_digits_dots_or_hyphens_is_kept_in_lowercase(string input, string value)
    {
        var key = BankAccountKey.Parse(input);

        Assert.NotNull(key);
        Assert.Equal((value, BankAccountKind.Alias, (string?)null), (key.Value, key.Kind, key.EntityCode));
    }

    [Theory]
    [InlineData("corto")]
    [InlineData("a.b-c.d-e.f-g.h-i.jkl")]
    [InlineData("juan perez")]
    [InlineData("juan_perez")]
    [InlineData("año.nuevo.mp")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_else_is_rejected(string? input) => Assert.Null(BankAccountKey.Parse(input));

    [Theory]
    // A no-break space, a narrow no-break space, a tab, an en dash and a minus sign, as a receipt or a copy and paste leaves them.
    [InlineData("2850 5909 4009 0418 1352 01")]
    [InlineData("2850 5909 4009 0418 1352 01")]
    [InlineData("28505909\t40090418135201")]
    [InlineData("2850590–9400904181352–01")]
    [InlineData("2850590−9400904181352−01")]
    public void Any_space_or_dash_between_the_digits_is_a_separator(string input) =>
        Assert.Equal("2850590940090418135201", BankAccountKey.Parse(input)?.Value);

    [Theory]
    // Twenty-two zeros add up but are no account, and neither is any key of only zeros.
    [InlineData("0000000000000000000000")]
    [InlineData("0000 0000 0000 0000 0000 00")]
    [InlineData("000000")]
    // Eleven digits are a CUIT or a CUIL, valid or not, and never an alias.
    [InlineData("20123456786")]
    [InlineData("20-12345678-6")]
    [InlineData("20123456787")]
    public void Zeros_or_a_cuit_are_not_an_account(string input) => Assert.Null(BankAccountKey.Parse(input));

    [Theory]
    // Whether an alias may be only digits could not be confirmed with the BCRA: digits of a length other than a CBU's or a CUIT's
    // are an alias.
    [InlineData("123456")]
    [InlineData("1234567890")]
    [InlineData("123456789012")]
    [InlineData("12345678901234567890")]
    public void Digits_of_another_length_are_an_alias(string input)
    {
        var key = BankAccountKey.Parse(input);

        Assert.NotNull(key);
        Assert.Equal((input, BankAccountKind.Alias), (key.Value, key.Kind));
    }

    [Fact]
    public void Two_ways_of_writing_the_same_account_are_the_same_key_and_its_text_is_masked()
    {
        Assert.Equal(BankAccountKey.Parse("2850590940090418135201"), BankAccountKey.Parse("2850 5909 4009 0418 1352 01"));
        Assert.Equal(BankAccountKey.Parse("Juan.Perez.MP"), BankAccountKey.Parse("juan.perez.mp"));
        Assert.Equal("285***5201", BankAccountKey.Parse("2850590940090418135201")!.ToString());
        Assert.Equal("ju***", BankAccountKey.Parse("juan.perez.mp")!.ToString());
    }
}
