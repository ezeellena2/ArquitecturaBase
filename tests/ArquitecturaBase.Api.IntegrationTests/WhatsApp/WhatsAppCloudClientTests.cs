using System.Net;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// El JSON que se le manda a Meta y cómo se leen sus respuestas, con la Graph API de mentira. Los formatos son los de
/// la documentación de Meta para la versión v25.0 (sección 9 del spec).
/// </summary>
public sealed class WhatsAppCloudClientTests
{
    private const string PhoneNumberId = "1234567890";
    private const string AccessToken = "test-access-token";
    private const string LoginCodeTemplate = "login_code_test";
    private const string InvitationTemplate = "invitation_test";
    private const string Code = "482913";
    private const string LinkUrl = "https://localhost:5173/ingresar#t=test-link-token";

    private static readonly PhoneNumber To = PhoneNumber.Create("+5493411234567").Value;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// La plantilla es la de <c>WhatsApp:Templates:LoginCode</c> y el idioma, el que pide el mensaje: el test usa un
    /// nombre distinto del de fábrica y "en", para que no pase con los valores por defecto. Meta cambia el botón
    /// "Copiar código" a tipo url al crear la plantilla: el código va dos veces.
    /// </summary>
    [Fact]
    public async Task Login_code_uses_the_configured_template_in_the_requested_language_with_the_code_in_the_body_and_the_url_button()
    {
        var request = await SendAsync(new WhatsAppLoginCodeMessage(To, "en", Code));

        Assert.NotEqual(LoginCodeTemplate, new WhatsAppTemplateOptions().LoginCode);
        AssertJson(
            """
            {
              "messaging_product": "whatsapp",
              "recipient_type": "individual",
              "to": "+5493411234567",
              "type": "template",
              "template": {
                "name": "login_code_test",
                "language": { "code": "en" },
                "components": [
                  { "type": "body", "parameters": [ { "type": "text", "text": "482913" } ] },
                  { "type": "button", "sub_type": "url", "index": "0", "parameters": [ { "type": "text", "text": "482913" } ] }
                ]
              }
            }
            """,
            request.Body);
    }

    /// <summary>
    /// La invitación sale con la plantilla de <c>WhatsApp:Templates:Invitation</c> (el test usa un nombre distinto del de
    /// fábrica): el nombre de la persona es <c>{{1}}</c>, el del sistema <c>{{2}}</c>, y el botón de respuesta rápida
    /// «Quiero entrar» lleva el payload que después vuelve en el webhook.
    /// </summary>
    [Fact]
    public async Task Invitation_uses_the_configured_template_with_the_name_the_system_and_the_payload_of_its_button()
    {
        var request = await SendAsync(new WhatsAppInvitationMessage(
            To, Guid.CreateVersion7(), Guid.CreateVersion7(), "en", "Laura Ríos", "Arquitectura Base", "WANT_TO_ENTER"));

        Assert.NotEqual(InvitationTemplate, new WhatsAppTemplateOptions().Invitation);
        AssertJson(
            """
            {
              "messaging_product": "whatsapp",
              "recipient_type": "individual",
              "to": "+5493411234567",
              "type": "template",
              "template": {
                "name": "invitation_test",
                "language": { "code": "en" },
                "components": [
                  {
                    "type": "body",
                    "parameters": [ { "type": "text", "text": "Laura Ríos" }, { "type": "text", "text": "Arquitectura Base" } ]
                  },
                  {
                    "type": "button",
                    "sub_type": "quick_reply",
                    "index": "0",
                    "parameters": [ { "type": "payload", "payload": "WANT_TO_ENTER" } ]
                  }
                ]
              }
            }
            """,
            request.Body);
    }

    [Fact]
    public async Task Link_button_is_an_interactive_cta_url_message()
    {
        var request = await SendAsync(
            new WhatsAppLinkButtonMessage(To, "Tocá Entrar para ingresar.", "Entrar", LinkUrl, footer: "Vence en 10 minutos."));

        AssertJson(
            """
            {
              "messaging_product": "whatsapp",
              "recipient_type": "individual",
              "to": "+5493411234567",
              "type": "interactive",
              "interactive": {
                "type": "cta_url",
                "body": { "text": "Tocá Entrar para ingresar." },
                "action": {
                  "name": "cta_url",
                  "parameters": { "display_text": "Entrar", "url": "https://localhost:5173/ingresar#t=test-link-token" }
                },
                "footer": { "text": "Vence en 10 minutos." }
              }
            }
            """,
            request.Body);
    }

    [Fact]
    public async Task Link_button_without_a_footer_leaves_the_footer_out()
    {
        var request = await SendAsync(new WhatsAppLinkButtonMessage(To, "Tocá Entrar para ingresar.", "Entrar", LinkUrl));

        Assert.Null(JsonNode.Parse(request.Body!)!["interactive"]!["footer"]);
    }

    [Fact]
    public async Task Reply_buttons_carry_each_button_with_its_id()
    {
        var request = await SendAsync(new WhatsAppReplyButtonsMessage(
            To,
            "¿Querés crear tu cuenta?",
            [new WhatsAppReplyButton("signup:yes", "Sí, crearla"), new WhatsAppReplyButton("signup:no", "No")],
            footer: "Arquitectura Base"));

        AssertJson(
            """
            {
              "messaging_product": "whatsapp",
              "recipient_type": "individual",
              "to": "+5493411234567",
              "type": "interactive",
              "interactive": {
                "type": "button",
                "body": { "text": "¿Querés crear tu cuenta?" },
                "footer": { "text": "Arquitectura Base" },
                "action": {
                  "buttons": [
                    { "type": "reply", "reply": { "id": "signup:yes", "title": "Sí, crearla" } },
                    { "type": "reply", "reply": { "id": "signup:no", "title": "No" } }
                  ]
                }
              }
            }
            """,
            request.Body);
    }

    [Fact]
    public async Task Text_is_a_text_message()
    {
        var request = await SendAsync(new WhatsAppTextMessage(To, "Hola, ¿cómo va?"));

        AssertJson(
            """
            {
              "messaging_product": "whatsapp",
              "recipient_type": "individual",
              "to": "+5493411234567",
              "type": "text",
              "text": { "body": "Hola, ¿cómo va?" }
            }
            """,
            request.Body);
    }

    [Fact]
    public async Task The_message_is_posted_to_the_version_and_phone_number_id_with_the_bearer_token()
    {
        var request = await SendAsync(new WhatsAppTextMessage(To, "Hola"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://graph.facebook.com/v25.0/1234567890/messages"), request.Uri);
        Assert.Equal("Bearer " + AccessToken, request.Authorization);
        Assert.Equal("+5493411234567", JsonNode.Parse(request.Body!)!["to"]!.GetValue<string>());
    }

    /// <summary>
    /// La lista de destinatarios del número de prueba de Meta guarda los celulares argentinos sin el 9 y rechaza el
    /// envío con el 9 (131030, confirmado en la prueba manual del Hito 1). Con la adaptación prendida, solo el "to" va
    /// sin el 9; el número sigue guardado con el 9 en todo lo demás.
    /// </summary>
    [Fact]
    public async Task Argentine_mobiles_go_without_the_nine_when_the_test_number_needs_it()
    {
        var request = await SendAsync(new WhatsAppTextMessage(To, "Hola"), sendArgentineMobilesWithoutNine: true);

        Assert.Equal("+543411234567", JsonNode.Parse(request.Body!)!["to"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_test_number_adaptation_leaves_other_countries_as_they_are()
    {
        var uruguay = PhoneNumber.Create("+59899123456").Value;

        var request = await SendAsync(new WhatsAppTextMessage(uruguay, "Hola"), sendArgentineMobilesWithoutNine: true);

        Assert.Equal("+59899123456", JsonNode.Parse(request.Body!)!["to"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_successful_send_returns_the_whatsapp_message_id()
    {
        var client = CreateClient(new FakeMetaHandler());

        var result = await client.SendAsync(new WhatsAppTextMessage(To, "Hola"), Ct);

        Assert.True(result.IsSent);
        Assert.Equal(FakeMetaHandler.WaMessageId, result.WaMessageId);
        Assert.Null(result.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 131030, "RecipientNotAllowed", false)]
    [InlineData(HttpStatusCode.BadRequest, 131047, "OutsideCustomerServiceWindow", false)]
    [InlineData(HttpStatusCode.BadRequest, 131026, "Undeliverable", false)]
    [InlineData(HttpStatusCode.BadRequest, 131056, "PairRateLimited", true)]
    [InlineData(HttpStatusCode.TooManyRequests, 130429, "RateLimited", true)]

    // Los errores de autorización de Meta, con el status con el que los manda: el token o sus permisos. Reintentar no
    // los arregla.
    [InlineData(HttpStatusCode.Unauthorized, 0, "InvalidToken", false)]
    [InlineData(HttpStatusCode.Unauthorized, 190, "InvalidToken", false)]
    [InlineData(HttpStatusCode.InternalServerError, 3, "MissingPermission", false)]
    [InlineData(HttpStatusCode.Forbidden, 10, "MissingPermission", false)]
    [InlineData(HttpStatusCode.Forbidden, 200, "MissingPermission", false)]
    [InlineData(HttpStatusCode.Forbidden, 299, "MissingPermission", false)]

    // Con un código que no está en la lista, el status: un 401 es el token y un 403, un permiso.
    [InlineData(HttpStatusCode.Unauthorized, 102, "InvalidToken", false)]
    [InlineData(HttpStatusCode.Forbidden, 131005, "MissingPermission", false)]

    // El código gana sobre el status. Con un 400 el status no ayuda: los decide solo el código, que es como Meta
    // documenta sus límites. Los de autorización van todos con 400, así el 401 y el 403 no los salvan.
    [InlineData(HttpStatusCode.BadRequest, 130429, "RateLimited", true)]
    [InlineData(HttpStatusCode.BadRequest, 0, "InvalidToken", false)]
    [InlineData(HttpStatusCode.BadRequest, 190, "InvalidToken", false)]
    [InlineData(HttpStatusCode.BadRequest, 10, "MissingPermission", false)]
    [InlineData(HttpStatusCode.BadRequest, 200, "MissingPermission", false)]
    [InlineData(HttpStatusCode.BadRequest, 299, "MissingPermission", false)]

    // Los bordes del tramo de permisos de la API.
    [InlineData(HttpStatusCode.BadRequest, 199, "Other", false)]
    [InlineData(HttpStatusCode.BadRequest, 300, "Other", false)]
    [InlineData(HttpStatusCode.InternalServerError, 131000, "Transient", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 131016, "Transient", true)]
    [InlineData(HttpStatusCode.BadRequest, 100, "Other", false)]
    public async Task Meta_errors_become_a_typed_failure_that_says_whether_to_retry(
        HttpStatusCode status,
        int metaCode,
        string expectedFailure,
        bool retryable)
    {
        var client = CreateClient(new FakeMetaHandler(_ => FakeMetaHandler.Error(status, metaCode)));

        var result = await client.SendAsync(new WhatsAppTextMessage(To, "Hola"), Ct);

        Assert.False(result.IsSent);
        Assert.Equal(Enum.Parse<WhatsAppSendFailure>(expectedFailure), result.Failure);
        Assert.Equal(retryable, result.Failure!.Value.IsRetryable());
        Assert.Equal(metaCode, result.MetaErrorCode);
        Assert.Null(result.WaMessageId);
    }

    /// <summary>
    /// Sin un cuerpo que se pueda leer, manda el status: un 401 es el token, un 403 un permiso y un 5xx, algo pasajero.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "InvalidToken")]
    [InlineData(HttpStatusCode.Forbidden, "MissingPermission")]
    [InlineData(HttpStatusCode.BadGateway, "Transient")]
    [InlineData(HttpStatusCode.TooManyRequests, "RateLimited")]
    [InlineData(HttpStatusCode.BadRequest, "Other")]
    public async Task Without_a_readable_error_the_status_code_decides(HttpStatusCode status, string expectedFailure)
    {
        var client = CreateClient(new FakeMetaHandler(_ => FakeMetaHandler.Json(status, "<html>Bad gateway</html>")));

        var result = await client.SendAsync(new WhatsAppTextMessage(To, "Hola"), Ct);

        Assert.Equal(Enum.Parse<WhatsAppSendFailure>(expectedFailure), result.Failure);
        Assert.Null(result.MetaErrorCode);
    }

    [Fact]
    public async Task Network_failures_and_timeouts_are_transient()
    {
        var unreachable = CreateClient(new FakeMetaHandler(_ => throw new HttpRequestException("Connection refused")));
        var timedOut = CreateClient(new FakeMetaHandler(_ => throw new TaskCanceledException("The request timed out")));

        var unreachableResult = await unreachable.SendAsync(new WhatsAppTextMessage(To, "Hola"), Ct);
        var timedOutResult = await timedOut.SendAsync(new WhatsAppTextMessage(To, "Hola"), Ct);

        Assert.Equal(WhatsAppSendFailure.Transient, unreachableResult.Failure);
        Assert.Equal(WhatsAppSendFailure.Transient, timedOutResult.Failure);
    }

    [Fact]
    public async Task A_successful_status_without_a_message_id_is_not_retried()
    {
        var client = CreateClient(new FakeMetaHandler(_ => FakeMetaHandler.Json(HttpStatusCode.OK, "{}")));

        var result = await client.SendAsync(new WhatsAppTextMessage(To, "Hola"), Ct);

        // Meta pudo haberlo mandado: reintentar podría duplicar el mensaje.
        Assert.False(result.IsSent);
        Assert.False(result.Failure!.Value.IsRetryable());
    }

    private static async Task<RecordedRequest> SendAsync(
        WhatsAppOutboundMessage message, bool sendArgentineMobilesWithoutNine = false)
    {
        var handler = new FakeMetaHandler();

        await CreateClient(handler, sendArgentineMobilesWithoutNine).SendAsync(message, Ct);

        return Assert.Single(handler.Requests);
    }

    private static WhatsAppCloudClient CreateClient(FakeMetaHandler handler, bool sendArgentineMobilesWithoutNine = false) =>
        new(
            new HttpClient(handler) { BaseAddress = WhatsAppCloudClient.GraphApiAddress },
            Options.Create(new WhatsAppOptions
            {
                PhoneNumberId = PhoneNumberId,
                AccessToken = AccessToken,
                Templates = new WhatsAppTemplateOptions { LoginCode = LoginCodeTemplate, Invitation = InvitationTemplate },
                SendArgentineMobilesWithoutNine = sendArgentineMobilesWithoutNine,
            }));

    private static void AssertJson(string expected, string? actual)
    {
        Assert.NotNull(actual);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), actual);
    }
}
