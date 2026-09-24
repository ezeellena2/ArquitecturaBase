using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.Abstractions.WhatsApp;

/// <summary>
/// Los límites de Meta que se controlan al armar el mensaje: pasarse es un error de programación, no algo que decide
/// la persona, así que es una excepción y no un Result.
/// </summary>
public sealed class WhatsAppOutboundMessageTests
{
    private const string Code = "482913";
    private const string LinkUrl = "https://localhost:5173/ingresar#t=secret-token";

    private static readonly PhoneNumber To = PhoneNumber.Create("+5493411234567").Value;

    [Fact]
    public void Reply_buttons_accept_from_one_to_three_buttons()
    {
        var one = new WhatsAppReplyButtonsMessage(To, "¿Querés entrar?", [Button("yes")]);
        var three = new WhatsAppReplyButtonsMessage(To, "¿Querés entrar?", [Button("a"), Button("b"), Button("c")]);

        Assert.Single(one.Buttons);
        Assert.Equal(3, three.Buttons.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Reply_buttons_reject_no_buttons_or_more_than_three(int count)
    {
        var buttons = Enumerable.Range(1, count).Select(number => Button("id-" + number)).ToList();

        Assert.Throws<ArgumentException>(() => new WhatsAppReplyButtonsMessage(To, "¿Querés entrar?", buttons));
    }

    [Fact]
    public void Reply_buttons_reject_repeated_ids_or_titles()
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppReplyButtonsMessage(
            To, "¿Querés entrar?", [new WhatsAppReplyButton("same", "Sí"), new WhatsAppReplyButton("same", "No")]));
        Assert.Throws<ArgumentException>(() => new WhatsAppReplyButtonsMessage(
            To, "¿Querés entrar?", [new WhatsAppReplyButton("yes", "Sí"), new WhatsAppReplyButton("other", "Sí")]));
    }

    [Theory]
    [InlineData("", "Sí")]
    [InlineData("yes", "")]
    [InlineData("yes", "Un título de más de veinte")]
    public void Reply_button_rejects_an_empty_id_or_a_title_outside_the_limits(string id, string title)
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppReplyButton(id, title));
    }

    [Fact]
    public void Link_button_rejects_a_relative_url_a_long_button_text_and_a_long_footer()
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppLinkButtonMessage(To, "Tocá Entrar", "Entrar", "/ingresar"));
        Assert.Throws<ArgumentException>(() =>
            new WhatsAppLinkButtonMessage(To, "Tocá Entrar", "Un texto de botón muy largo", LinkUrl));
        Assert.Throws<ArgumentException>(() =>
            new WhatsAppLinkButtonMessage(To, "Tocá Entrar", "Entrar", LinkUrl, footer: new string('x', 61)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567890123456")]
    public void Login_code_rejects_an_empty_code_or_one_longer_than_fifteen_characters(string code)
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppLoginCodeMessage(To, "es", code));
    }

    [Fact]
    public void Text_rejects_an_empty_body()
    {
        Assert.Throws<ArgumentException>(() => new WhatsAppTextMessage(To, " "));
    }

    [Fact]
    public void The_safe_summary_never_carries_the_code_or_the_link()
    {
        var loginCode = new WhatsAppLoginCodeMessage(To, "es", Code);
        var link = new WhatsAppLinkButtonMessage(To, "Tocá Entrar para ingresar.", "Entrar", LinkUrl);

        Assert.Equal("[código]", loginCode.SafeSummary);
        Assert.DoesNotContain(LinkUrl, link.SafeSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", link.SafeSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_invitation_is_summarized_without_the_name_and_keeps_the_name_in_a_single_line()
    {
        // Meta rechaza un parámetro de plantilla con saltos de línea o muchos espacios seguidos.
        var invitation = Invitation(name: "  Laura \n  Ríos\t ");

        Assert.Equal("[invitación]", invitation.SafeSummary);
        Assert.Equal("Laura Ríos", invitation.Name);
        Assert.Equal("es", invitation.LanguageCode);
        Assert.Equal("Arquitectura Base", invitation.AppName);
        Assert.Equal("WANT_TO_ENTER", invitation.ReplyPayload);
    }

    [Fact]
    public void The_invitation_needs_the_name_the_system_the_language_the_button_and_its_ids()
    {
        Assert.Throws<ArgumentException>(() => Invitation(name: " "));
        Assert.Throws<ArgumentException>(() => Invitation(appName: ""));
        Assert.Throws<ArgumentException>(() => Invitation(languageCode: ""));
        Assert.Throws<ArgumentException>(() => Invitation(replyPayload: ""));
        Assert.Throws<ArgumentException>(() => Invitation(userId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Invitation(invitationId: Guid.Empty));
    }

    /// <summary>
    /// Un record imprime todas sus propiedades en ToString: si un mensaje terminara en un log, dejaría a la vista el
    /// código, el enlace y el número.
    /// </summary>
    [Fact]
    public void ToString_does_not_print_the_code_the_link_or_the_number()
    {
        WhatsAppOutboundMessage[] messages =
        [
            new WhatsAppLoginCodeMessage(To, "es", Code),
            new WhatsAppLinkButtonMessage(To, "Tocá Entrar para ingresar.", "Entrar", LinkUrl),
            new WhatsAppTextMessage(To, "Hola"),
            new WhatsAppReplyButtonsMessage(To, "¿Querés entrar?", [Button("yes")]),
            Invitation(),
        ];

        Assert.All(messages, message =>
        {
            var printed = message.ToString();

            Assert.DoesNotContain(Code, printed, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-token", printed, StringComparison.Ordinal);
            Assert.DoesNotContain(To.Value, printed, StringComparison.Ordinal);
        });
    }

    private static WhatsAppReplyButton Button(string id) => new(id, "Opción " + id);

    private static WhatsAppInvitationMessage Invitation(
        Guid? userId = null,
        Guid? invitationId = null,
        string languageCode = "es",
        string name = "Laura Ríos",
        string appName = "Arquitectura Base",
        string replyPayload = "WANT_TO_ENTER") =>
        new(To, userId ?? Guid.CreateVersion7(), invitationId ?? Guid.CreateVersion7(), languageCode, name, appName, replyPayload);
}
