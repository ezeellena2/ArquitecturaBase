using System.Net;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// H9: every client of Meta tells /health what it answered about the token. A rejected token is one, and any call that works clears
// it; a missing permission belongs to its family (messages, media and uploads; or Flows and phone_numbers), and only a call of that
// family clears it.
public sealed class WhatsAppHealthTests
{
    [Fact]
    public void A_missing_permission_of_one_family_is_not_cleared_by_a_call_of_the_other()
    {
        var health = new WhatsAppHealth();

        health.Observe(HttpStatusCode.Forbidden, WhatsAppTokenUse.Management);
        health.Observe(HttpStatusCode.OK, WhatsAppTokenUse.Messaging);

        Assert.Equal("MissingPermission", health.TokenProblem?.ToString());
        Assert.Equal((false, true), health.MissingPermissions);
        health.Observe(HttpStatusCode.OK, WhatsAppTokenUse.Management);
        Assert.Null(health.TokenProblem);
    }

    [Fact]
    public void A_rejected_token_is_cleared_by_any_call_that_works()
    {
        var health = new WhatsAppHealth();

        health.Observe(HttpStatusCode.Unauthorized, WhatsAppTokenUse.Messaging);
        Assert.Equal("InvalidToken", health.TokenProblem?.ToString());
        health.Observe(HttpStatusCode.OK, WhatsAppTokenUse.Management);

        Assert.Null(health.TokenProblem);
    }

    [Fact]
    public void Another_status_says_nothing_about_the_token()
    {
        var health = new WhatsAppHealth();
        health.Observe(HttpStatusCode.Forbidden, WhatsAppTokenUse.Messaging);

        health.Observe(HttpStatusCode.ServiceUnavailable, WhatsAppTokenUse.Messaging);
        health.Observe(HttpStatusCode.NotFound, WhatsAppTokenUse.Messaging);

        Assert.Equal("MissingPermission", health.TokenProblem?.ToString());
    }
}
