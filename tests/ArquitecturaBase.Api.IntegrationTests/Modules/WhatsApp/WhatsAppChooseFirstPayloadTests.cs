using System.Text.Json.Nodes;
using System.Web;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// LD2-A (2026-11-07), profile r11: the first screen of a form gets the values of a question that chooses first by a field, each
// with the update_data that fills and shows its list, which starts empty and hidden. The official preview sends the same data.
public sealed class WhatsAppChooseFirstPayloadTests
{
    private const string Key = "field_0195f0aa00007000800000000000a001";
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> NoValues = [];

    private static StructuredFormCatalogOption Option(string text, string? group, string? detail = null) =>
        new(Guid.CreateVersion7(), text, detail, Group: group);

    private static StructuredFormCatalogOffer Offer(IReadOnlyList<StructuredFormCatalogOption> options, string label,
        params StructuredFormCatalogGroup[] groups) =>
        new(Guid.CreateVersion7(), Key, "Artículo", Guid.CreateVersion7(), 1, "Manual", "Insumos", options, Grouping: new(label, groups));

    private static string[] Ids(JsonNode? options) => [.. options!.AsArray().Select(option => (string)option!["id"]!)];

    [Fact]
    public void The_first_screen_gets_each_value_with_the_update_data_that_fills_its_list()
    {
        var glifosato = Option("Glifosato", "valor-h", "Bidón de 20 L");
        var atrazina = Option("Atrazina", "valor-h");
        var cipermetrina = Option("Cipermetrina", "valor-i");
        var loose = Option("Suelto", null);
        var offer = Offer([glifosato, atrazina, cipermetrina, loose], "Rubro",
            new("valor-h", "Herbicidas"), new("valor-i", "Insecticidas"), new(null, "Sin rubro"));

        var data = WhatsAppFlowNavigation.Build("FORM", [offer], NoValues)["data"]!.AsObject();

        Assert.Equal([Key + "_groups", Key + "_group_label", Key + "_options", Key + "_visible"], data.Select(pair => pair.Key));
        var groups = data[Key + "_groups"]!.AsArray();
        Assert.Equal(["g1", "g2", "g3"], groups.Select(group => (string)group!["id"]!));
        Assert.Equal(["Herbicidas", "Insecticidas", "Sin rubro"], groups.Select(group => (string)group!["title"]!));
        var action = groups[0]!["on-select-action"]!;
        Assert.Equal("update_data", (string)action["name"]!);
        Assert.Equal([glifosato.Id.ToString("D"), atrazina.Id.ToString("D")], Ids(action["payload"]![Key + "_options"]));
        Assert.Equal("Bidón de 20 L", (string)action["payload"]![Key + "_options"]![0]!["description"]!);
        Assert.True((bool)action["payload"]![Key + "_visible"]!);
        Assert.Equal([cipermetrina.Id.ToString("D")], Ids(groups[1]!["on-select-action"]!["payload"]![Key + "_options"]));
        Assert.Equal([loose.Id.ToString("D")], Ids(groups[2]!["on-select-action"]!["payload"]![Key + "_options"]));
        Assert.Equal("Rubro", (string)data[Key + "_group_label"]!);
        Assert.Empty(data[Key + "_options"]!.AsArray());
        Assert.False((bool)data[Key + "_visible"]!);
        // What is chosen in the first dropdown is not part of the answer: the values travel with ids of the message.
        Assert.DoesNotContain("valor-", data.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_name_and_the_values_fit_the_limits_of_a_dropdown()
    {
        var offer = Offer([Option("Uno", "largo")], "Nombre de campo bastante largo",
            new StructuredFormCatalogGroup("largo", "Un valor de texto que pasa los treinta caracteres"));

        var data = WhatsAppFlowNavigation.Build("FORM", [offer], NoValues)["data"]!;

        Assert.Equal("Nombre de campo bas…", (string)data[Key + "_group_label"]!);
        Assert.Equal("Un valor de texto que pasa lo…", (string)data[Key + "_groups"]![0]!["title"]!);
    }

    // The data travels in the link: a grouped list keeps its first values, each with its first options.
    [Fact]
    public void The_official_preview_keeps_fewer_values_and_options_when_the_link_would_grow_too_long()
    {
        var groups = Enumerable.Range(0, 20).Select(index => new StructuredFormCatalogGroup("valor-" + index, "Rubro " + index)).ToArray();
        var options = groups.SelectMany(group => Enumerable.Range(0, 20).Select(index => Option("Opción " + index, group.Value, new string('x', 100)))).ToArray();

        var url = WhatsAppFlowNavigation.InteractivePreview("https://business.facebook.com/wa/manage/flows/333/preview/?token=private", "FORM",
            [Offer(options, "Rubro", groups)]);

        var escaped = url[(url.IndexOf("flow_action_payload=", StringComparison.Ordinal) + "flow_action_payload=".Length)..];
        Assert.InRange(escaped.Length, 1, WhatsAppFlowNavigation.PreviewPayloadLimit);
        var data = JsonNode.Parse(HttpUtility.ParseQueryString(new Uri(url).Query)["flow_action_payload"]!)!["data"]!;
        var kept = data[Key + "_groups"]!.AsArray();
        Assert.InRange(kept.Count, 1, 19);
        Assert.Equal(groups.Take(kept.Count).Select(group => group.Text), kept.Select(group => (string)group!["title"]!));
        for (var index = 0; index < kept.Count; index++)
            Assert.Equal(options.Where(option => option.Group == groups[index].Value).Take(kept.Count).Select(option => option.Id.ToString("D")),
                Ids(kept[index]!["on-select-action"]!["payload"]![Key + "_options"]));
        Assert.Empty(data[Key + "_options"]!.AsArray());
    }
}
