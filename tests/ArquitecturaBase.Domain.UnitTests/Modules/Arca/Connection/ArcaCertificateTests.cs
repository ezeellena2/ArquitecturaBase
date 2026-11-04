using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Connection;

public sealed class ArcaCertificateTests
{
    private static readonly DateTime From = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2028, 11, 2, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_request_keeps_its_id_and_waits_as_pending_until_the_certificate_is_loaded()
    {
        var id = Guid.CreateVersion7();
        var certificate = Request(id);

        Assert.Equal(id, certificate.Id);
        Assert.Equal(ArcaCertificateRole.Pending, certificate.Role);
        Assert.False(certificate.IsLoaded);
        Assert.Equal("arquitecturabase", certificate.Alias);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("1arquitectura")]
    [InlineData("Arquitectura")]
    [InlineData("arquitectura base")]
    [InlineData("arquitectura_base")]
    [InlineData("abcdefghijabcdefghijabcdefghija")]
    public void An_alias_has_3_to_30_lowercase_letters_digits_or_hyphens_and_starts_with_a_letter(string alias)
    {
        var result = ArcaCertificate.Request(Guid.CreateVersion7(), Guid.CreateVersion7(), alias, "REQUEST", "PROTECTED");
        Assert.Equal(ArcaConnectionErrors.AliasInvalid, result.Error);
    }

    [Fact]
    public void Loading_and_promoting_make_it_the_active_certificate()
    {
        var certificate = Request(Guid.CreateVersion7());

        certificate.Load("CERTIFICATE", From, To, "THUMBPRINT");
        Assert.True(certificate.IsLoaded);
        Assert.Equal(ArcaCertificateRole.Pending, certificate.Role);

        certificate.Promote();
        Assert.Equal(ArcaCertificateRole.Active, certificate.Role);
        Assert.Equal(To, certificate.NotAfterUtc);
    }

    [Fact]
    public void Promoting_a_certificate_that_was_not_loaded_is_a_bug()
    {
        Assert.Throws<InvalidOperationException>(() => Request(Guid.CreateVersion7()).Promote());
    }

    [Fact]
    public void It_expires_at_its_not_after_date()
    {
        var certificate = Request(Guid.CreateVersion7());
        certificate.Load("CERTIFICATE", From, To, "THUMBPRINT");

        Assert.False(certificate.IsExpired(To.AddSeconds(-1)));
        Assert.True(certificate.IsExpired(To));
        Assert.True(certificate.ExpiresWithin(TimeSpan.FromDays(30), To.AddDays(-30)));
        Assert.False(certificate.ExpiresWithin(TimeSpan.FromDays(30), To.AddDays(-31)));
    }

    [Fact]
    public void The_text_representation_never_shows_the_key_or_the_documents()
    {
        var certificate = Request(Guid.CreateVersion7());
        certificate.Load("CERTIFICATE", From, To, "THUMBPRINT");

        var text = certificate.ToString();
        Assert.DoesNotContain("PROTECTED", text, StringComparison.Ordinal);
        Assert.DoesNotContain("REQUEST", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CERTIFICATE", text, StringComparison.Ordinal);
    }

    private static ArcaCertificate Request(Guid id) =>
        ArcaCertificate.Request(id, Guid.CreateVersion7(), "arquitecturabase", "REQUEST", "PROTECTED").Value;
}
