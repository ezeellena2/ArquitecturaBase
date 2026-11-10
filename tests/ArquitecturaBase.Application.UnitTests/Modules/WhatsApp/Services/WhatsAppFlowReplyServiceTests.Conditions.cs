using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// r13: a calculation never arrives (CA1-A) and a question hidden by its condition is left out (CO1-A), as the app does not send
// what was written in a hidden question; the owner computes the first and accepts the second without an answer.
public sealed partial class WhatsAppFlowReplyServiceTests
{
    private static readonly Guid TotalId = Guid.Parse("00000000-0000-7000-8000-0000000000f1");

    // «¿Tiene descuento?» shows the discount and its kind; the kind shows a note; the total is computed.
    private static IReadOnlyList<WhatsAppFlowProjectionField> ConditionMapping(Fixture fixture) =>
    [
        fixture.Mapping[4],
        fixture.Mapping[2] with { ShowWhen = new(fixture.Mapping[4].FieldId, ["true"]) },
        fixture.Mapping[5] with { ShowWhen = new(fixture.Mapping[4].FieldId, ["true"]) },
        fixture.Mapping[0] with { ShowWhen = new(fixture.Mapping[5].FieldId, [fixture.OptionId.ToString("D")]) },
        new(TotalId, "field_" + TotalId.ToString("N"), "decimal", false, null, [], Calculated: true),
    ];

    private static void UseConditions(Fixture fixture, IReadOnlyList<WhatsAppFlowProjectionField> mapping) =>
        fixture.ProjectionProtector.Projection = fixture.Projection with { ProfileRevision = WhatsAppFlowCompiler.ProfileRevision, Mapping = mapping };

    private static Dictionary<string, string> ConditionReply(IReadOnlyList<WhatsAppFlowProjectionField> mapping, Fixture fixture, string discount) =>
        new(StringComparer.Ordinal)
        {
            [mapping[0].Key] = discount, [mapping[1].Key] = "12,5", [mapping[2].Key] = fixture.OptionId.ToString("D"), [mapping[3].Key] = "Promo",
        };

    [Fact]
    public async Task Questions_shown_by_their_answers_reach_the_owner_and_the_calculation_is_left_to_it()
    {
        var fixture = new Fixture();
        var mapping = ConditionMapping(fixture);
        UseConditions(fixture, mapping);
        var message = fixture.AddReply("wamid.conditions-shown", ConditionReply(mapping, fixture, "true"));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        var request = Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request);
        Assert.Equal(new[] { mapping[0].Key, mapping[1].Key, mapping[2].Key, mapping[3].Key }.Order(StringComparer.Ordinal),
            request.Answers.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("12.5", request.Answers[mapping[1].Key]);
    }

    // Back with the arrow, the person changed the answer that showed them: what they had written there does not reach the owner,
    // nor what depends on a question that is now hidden.
    [Fact]
    public async Task What_was_written_in_questions_that_are_now_hidden_is_left_out()
    {
        var fixture = new Fixture();
        var mapping = ConditionMapping(fixture);
        UseConditions(fixture, mapping);
        var message = fixture.AddReply("wamid.conditions-hidden", ConditionReply(mapping, fixture, "false"));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        var request = Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request);
        Assert.Equal([mapping[0].Key], request.Answers.Keys);
        Assert.Equal("false", request.Answers[mapping[0].Key]);
    }

    [Fact]
    public async Task A_required_question_that_shows_and_did_not_arrive_is_rejected_before_the_owner()
    {
        var fixture = new Fixture();
        var mapping = ConditionMapping(fixture);
        UseConditions(fixture, mapping);
        var answers = ConditionReply(mapping, fixture, "true");
        answers.Remove(mapping[1].Key);
        var message = fixture.AddReply("wamid.conditions-missing", answers);

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(FlowErrors.InvalidValue.Code, message.FlowCaptureErrorCode);
        Assert.Equal(0, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task An_answer_for_a_calculation_is_not_something_the_flow_sends_and_is_rejected_before_the_owner()
    {
        var fixture = new Fixture();
        var mapping = ConditionMapping(fixture);
        UseConditions(fixture, mapping);
        var answers = ConditionReply(mapping, fixture, "true");
        answers[mapping[4].Key] = "100";
        var message = fixture.AddReply("wamid.conditions-calculated", answers);

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(FlowErrors.InvalidValue.Code, message.FlowCaptureErrorCode);
        Assert.Equal(0, fixture.Sink.CaptureCount);
    }
}
