using System.Text;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

public sealed class ArcaTicketTokenTests
{
    [Fact]
    public void The_relations_are_the_cuits_the_ticket_represents()
    {
        var token = Token("""<relations><relation key="30719998883" reltype="4"/><relation key="30710001231" reltype="4"/></relations>""");

        var relations = ArcaTicketToken.ReadRelations(token);

        Assert.NotNull(relations);
        Assert.Equal(["30710001231", "30719998883"], relations.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_ticket_without_relations_represents_nobody()
    {
        var relations = ArcaTicketToken.ReadRelations(Token(string.Empty));

        Assert.NotNull(relations);
        Assert.Empty(relations);
    }

    [Theory]
    [InlineData("not base64 at all")]
    [InlineData("PGJyb2tlbg==")] // "<broken"
    [InlineData("PG90aGVyLz4=")] // "<other/>"
    public void A_token_that_cannot_be_read_has_no_relations_to_tell(string token) => Assert.Null(ArcaTicketToken.ReadRelations(token));

    [Fact]
    public void A_token_with_a_document_type_is_not_read()
    {
        const string xml = """<?xml version="1.0"?><!DOCTYPE sso [<!ENTITY x "30710001231">]><sso version="2.0"><operation><login><relations><relation key="&x;"/></relations></login></operation></sso>""";

        Assert.Null(ArcaTicketToken.ReadRelations(Convert.ToBase64String(Encoding.UTF8.GetBytes(xml))));
    }

    // La forma del token del WSAA: un <sso> en base64 con el login y sus relaciones.
    private static string Token(string relations) => Convert.ToBase64String(Encoding.UTF8.GetBytes(
        $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><sso version="2.0"><id src="CN=wsaa, O=AFIP, C=AR, SERIALNUMBER=CUIT 33693450239" dst="CN=wsfe, O=AFIP, C=AR" unique_id="1" gen_time="1794225600" exp_time="1794268800"/><operation type="login" value="granted"><login entity="33693450239" service="wsfe" uid="SERIALNUMBER=CUIT 30719998883, CN=plataforma-lostilos" authmethod="cms" regmethod="22">{relations}</login></operation></sso>"""));
}
