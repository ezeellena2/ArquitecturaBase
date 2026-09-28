using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

/// <summary>
/// La auditoría de los tres ingresos: el IP y el navegador de la petición, y la hora al escribir, no la de quien
/// construyó el recorder. Los tests de cada ingreso fijan qué fila deja cada caso.
/// </summary>
public sealed class LoginAuditRecorderTests
{
    private readonly InMemoryLoginAuditRepository _audits = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeRequestInfo _request = new() { IpAddress = "198.51.100.7", UserAgent = "browser" };

    [Fact]
    public void Success_keeps_the_request_and_the_time_of_writing()
    {
        var recorder = new LoginAuditRecorder(_audits, _request, _clock);
        var userId = Guid.CreateVersion7();
        _clock.Advance(TimeSpan.FromSeconds(5));

        recorder.Succeeded("ana@example.com", userId, LoginMethod.Google);

        var audit = Assert.Single(_audits.Audits);
        Assert.True(audit.Succeeded);
        Assert.Equal("ana@example.com", audit.Identifier);
        Assert.Equal(userId, audit.UserId);
        Assert.Equal(LoginMethod.Google, audit.Method);
        Assert.Null(audit.FailureReason);
        Assert.Equal("198.51.100.7", audit.IpAddress);
        Assert.Equal("browser", audit.UserAgent);
        Assert.Equal(new DateTime(2026, 9, 28, 12, 0, 5, DateTimeKind.Utc), audit.OccurredAtUtc);
    }

    [Fact]
    public void Failure_keeps_the_error_code_and_returns_the_same_error()
    {
        var recorder = new LoginAuditRecorder(_audits, _request, _clock);

        var error = recorder.Failed("+5493515550101", userId: null, LoginMethod.WhatsAppLink, AccountErrors.Disabled);

        Assert.Same(AccountErrors.Disabled, error);
        var audit = Assert.Single(_audits.Audits);
        Assert.False(audit.Succeeded);
        Assert.Null(audit.UserId);
        Assert.Equal(LoginMethod.WhatsAppLink, audit.Method);
        Assert.Equal(AccountErrors.DisabledCode, audit.FailureReason);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, audit.OccurredAtUtc);
    }
}
