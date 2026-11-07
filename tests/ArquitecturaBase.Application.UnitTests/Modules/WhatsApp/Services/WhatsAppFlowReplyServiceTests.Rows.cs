using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppFlowReplyServiceTests
{
    private static readonly Guid BlockId = Guid.Parse("00000000-0000-7000-8000-0000000000e1");
    private static readonly Guid AmountId = Guid.Parse("00000000-0000-7000-8000-0000000000e2");
    private static readonly Guid RemarkId = Guid.Parse("00000000-0000-7000-8000-0000000000e3");

    // Two questions outside the block and a block of 1 to 10 items with three questions, as in the version tried on a phone.
    private static IReadOnlyList<WhatsAppFlowProjectionField> RowsMapping(Fixture fixture, int minimum = 1, WhatsAppFlowProjectionField? extraBlock = null)
    {
        IReadOnlyList<WhatsAppFlowProjectionField> mapping =
        [
            fixture.Mapping[0],
            fixture.Mapping[3],
            new(BlockId, "field_" + BlockId.ToString("N"), "group", minimum > 0, null, [], Rows: new(minimum, 10)),
            fixture.Mapping[5] with { Repeated = true },
            new(AmountId, "field_" + AmountId.ToString("N"), "decimal", true, null, [], Repeated: true),
            new(RemarkId, "field_" + RemarkId.ToString("N"), "shortText", true, 80, [], Repeated: true),
        ];
        return extraBlock is null ? mapping : [.. mapping, extraBlock];
    }

    private static void UseRows(Fixture fixture, IReadOnlyList<WhatsAppFlowProjectionField> mapping) =>
        fixture.ProjectionProtector.Projection = fixture.Projection with { ProfileRevision = "flow-static-7.3-r10", Mapping = mapping };

    private static Dictionary<string, string> PhoneReply(Fixture fixture, IReadOnlyList<WhatsAppFlowProjectionField> mapping)
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [mapping[0].Key] = "Proveedor del norte",
            [mapping[1].Key] = "2026-11-07",
        };
        foreach (var row in new[] { 1, 2, 3 })
        {
            answers[mapping[3].Key + "_r" + row] = fixture.OptionId.ToString("D");
            answers[mapping[4].Key + "_r" + row] = (100 * row).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",5";
            answers[mapping[5].Key + "_r" + row] = "Sin daños " + row;
            answers[mapping[2].Key + "_r" + row] = row < 3 ? "true" : "false";
        }
        return answers;
    }

    [Fact]
    public async Task A_reply_with_three_items_reaches_the_owner_as_the_items_of_its_block()
    {
        var fixture = new Fixture();
        var mapping = RowsMapping(fixture);
        UseRows(fixture, mapping);
        var answers = PhoneReply(fixture, mapping);
        Assert.Equal(14, answers.Count);
        var message = fixture.AddReply("wamid.rows-phone", answers);

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        var request = Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request);
        Assert.Equal(new[] { mapping[0].Key, mapping[1].Key, mapping[2].Key }.Order(StringComparer.Ordinal),
            request.Answers.Keys.Order(StringComparer.Ordinal));
        var items = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(request.Answers[mapping[2].Key])!;
        Assert.Equal(3, items.Count);
        Assert.Equal(["100.5", "200.5", "300.5"], items.Select(item => item[mapping[4].Key]));
        Assert.All(items, item => Assert.Equal([mapping[3].Key, mapping[4].Key, mapping[5].Key], item.Keys));
        Assert.Equal("Sin daños 2", items[1][mapping[5].Key]);
    }

    [Fact]
    public async Task A_catalog_question_of_the_block_takes_the_offer_sealed_in_the_session()
    {
        var fixture = new Fixture(catalog: true);
        var mapping = RowsMapping(fixture);
        UseRows(fixture, mapping);
        var message = fixture.AddReply("wamid.rows-catalog", PhoneReply(fixture, mapping));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        var request = Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request);
        Assert.Equal(fixture.SessionProtector.Payload.Catalogs, request.Catalogs);
        var items = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(request.Answers[mapping[2].Key])!;
        Assert.All(items, item => Assert.Equal(fixture.OptionId.ToString("D"), item[mapping[3].Key]));
    }

    [Fact]
    public async Task A_block_with_minimum_zero_answered_with_no_reaches_the_owner_without_items()
    {
        var fixture = new Fixture();
        var mapping = RowsMapping(fixture, minimum: 0);
        UseRows(fixture, mapping);
        var message = fixture.AddReply("wamid.rows-none", new Dictionary<string, string>
        {
            [mapping[0].Key] = "Proveedor", [mapping[1].Key] = "2026-11-07", [mapping[2].Key + "_r0"] = "false",
        });

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        Assert.Equal("[]", Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request).Answers[mapping[2].Key]);
    }

    [Fact]
    public async Task Items_that_the_flow_cannot_send_are_rejected_before_the_owner()
    {
        var fixture = new Fixture();
        var mapping = RowsMapping(fixture);
        UseRows(fixture, mapping);
        var answers = PhoneReply(fixture, mapping);
        answers.Remove(mapping[2].Key + "_r1");
        var message = fixture.AddReply("wamid.rows-invalid", answers);

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(FlowErrors.InvalidValue.Code, message.FlowCaptureErrorCode);
        Assert.Equal(0, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_projection_this_app_cannot_read_is_rejected_with_the_usual_notice_instead_of_being_unrelated()
    {
        var fixture = new Fixture();
        var other = Guid.Parse("00000000-0000-7000-8000-0000000000e9");
        var mapping = RowsMapping(fixture, extraBlock: new(other, "field_" + other.ToString("N"), "group", false, null, [], Rows: new(0, 2)));
        UseRows(fixture, mapping);
        var message = fixture.AddReply("wamid.rows-unreadable", PhoneReply(fixture, mapping));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(FlowErrors.InvalidValue.Code, message.FlowCaptureErrorCode);
        Assert.Equal(0, fixture.Sink.CaptureCount);
    }
}
