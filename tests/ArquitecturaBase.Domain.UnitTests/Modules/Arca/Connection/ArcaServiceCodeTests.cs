using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Connection;

public sealed class ArcaServiceCodeTests
{
    [Theory]
    [InlineData("wsfe")]
    [InlineData("ws_sr_constancia_inscripcion")]
    [InlineData("wscpe")]
    public void A_wsaa_service_name_is_a_valid_code(string value)
    {
        var code = ArcaServiceCode.Create(value);
        Assert.NotNull(code);
        Assert.Equal(value, code.Value.Value);
        Assert.Equal(value, code.Value.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("w")]
    [InlineData("WSFE")]
    [InlineData("ws fe")]
    [InlineData("ws-fe")]
    [InlineData("abcdefghijabcdefghijabcdefghijabcdefghijx")]
    public void Anything_else_is_not_a_code(string? value) => Assert.Null(ArcaServiceCode.Create(value));

    [Fact]
    public void Two_codes_with_the_same_name_are_equal() =>
        Assert.Equal(ArcaServiceCode.Create("wsfe"), ArcaServiceCode.Create("wsfe"));
}
