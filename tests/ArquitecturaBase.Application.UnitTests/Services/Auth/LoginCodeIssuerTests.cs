using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

/// <summary>
/// El emisor de códigos del núcleo, el único que emite: los límites por destino, sea cual sea el canal. El tope diario de
/// un canal que se paga es de su módulo (con WhatsApp, WhatsAppCodeQuotaGuardTests). Los límites por destino y el orden
/// con el lock los prueban los servicios que lo usan.
/// </summary>
public sealed class LoginCodeIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_phone_code_has_no_daily_limit_in_the_core()
    {
        var codes = new InMemoryLoginCodeRepository();
        foreach (var other in new[] { "+5493515550101", "+5493515550102", "+5493515550103" })
        {
            var sent = LoginCode.Issue(
                LoginCodeDestination.ForPhone(PhoneNumber.Create(other).Value), LoginCodePurpose.SignIn, requestedByUserId: null,
                "hash", Now.UtcDateTime.AddMinutes(-5), TimeSpan.FromMinutes(10), maxAttempts: 5);
            sent.MarkSent(Now.UtcDateTime.AddMinutes(-5));
            codes.Add(sent);
        }

        var issued = await new LoginCodeIssuer(
                codes,
                new FakeLoginCodeGenerator(),
                new FakeLoginCodeHasher(),
                Options.Create(new LoginCodeOptions()),
                new FakeTimeProvider(Now))
            .IssueSignInCodeAsync(LoginCodeDestination.ForPhone(PhoneNumber.Create("+5493515550199").Value), Ct);

        Assert.True(issued.IsSuccess);
        Assert.Equal(["+5493515550199"], codes.LockedDestinations);
        Assert.Equal(4, codes.Codes.Count);
    }
}
