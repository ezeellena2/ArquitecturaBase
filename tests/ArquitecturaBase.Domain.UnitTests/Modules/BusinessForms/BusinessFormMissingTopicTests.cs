using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;
public sealed class BusinessFormMissingTopicTests
{
    private static readonly DateTime AtUtc = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void A_topic_needs_only_its_original_language_and_normalizes_an_equivalent_name(string culture)
    {
        var result = BusinessFormMissingTopic.Create("  Préstamo   de herramientas  ", "  Registrar un préstamo de herramientas.  ", culture);
        Assert.True(result.IsSuccess); var topic = result.Value;
        Assert.Equal("Préstamo   de herramientas", topic.Name); Assert.Equal("PRÉSTAMO DE HERRAMIENTAS", topic.NormalizedName);
        Assert.Equal("Registrar un préstamo de herramientas.", topic.Description); Assert.Equal(culture, topic.OriginalCulture);
        Assert.Equal(1, topic.Revision); Assert.Equal(BusinessFormMissingTopicState.Pending, topic.State);
    }
    [Theory]
    [InlineData("", "Descripción", "es")]
    [InlineData("Tema", " ", "en")]
    [InlineData("Tema", "Descripción", "fr")]
    [InlineData("bad\0name", "Descripción", "es")]
    public void Invalid_topic_content_is_rejected(string name, string description, string culture)
    {
        var result = BusinessFormMissingTopic.Create(name, description, culture);
        Assert.True(result.IsFailure); Assert.Equal(FormMissingRequestErrors.ContentInvalid, result.Error);
    }
    [Fact]
    public void A_new_request_advances_the_revision_of_an_existing_pending_topic()
    {
        var topic = BusinessFormMissingTopic.Create("Horas extra", "Registrar horas trabajadas fuera de horario.", "es").Value;
        Assert.True(topic.RegisterRequest().IsSuccess); Assert.Equal(2, topic.Revision); Assert.Equal(BusinessFormMissingTopicState.Pending, topic.State);
    }
    [Fact]
    public void Discard_keeps_the_actual_actor_and_utc_time_under_the_expected_revision()
    {
        var topic = BusinessFormMissingTopic.Create("Horas extra", "Registrar horas trabajadas fuera de horario.", "es").Value;
        var actor = Guid.NewGuid(); Assert.True(topic.Discard(1, actor, AtUtc).IsSuccess);
        Assert.Equal(2, topic.Revision); Assert.Equal(BusinessFormMissingTopicState.Discarded, topic.State);
        Assert.Equal(actor, topic.DiscardedByUserId); Assert.Equal(AtUtc, topic.DiscardedAtUtc);
    }
    [Fact]
    public void A_new_request_reopens_a_discarded_topic_and_preserves_the_last_discard_fact()
    {
        var topic = BusinessFormMissingTopic.Create("Horas extra", "Registrar horas trabajadas fuera de horario.", "es").Value;
        var actor = Guid.NewGuid(); Assert.True(topic.Discard(1, actor, AtUtc).IsSuccess);
        Assert.True(topic.RegisterRequest().IsSuccess); Assert.Equal(3, topic.Revision); Assert.Equal(BusinessFormMissingTopicState.Pending, topic.State);
        Assert.Equal(actor, topic.DiscardedByUserId); Assert.Equal(AtUtc, topic.DiscardedAtUtc);
    }
    [Fact]
    public void A_stale_discard_cannot_hide_a_new_request()
    {
        var topic = BusinessFormMissingTopic.Create("Horas extra", "Registrar horas trabajadas fuera de horario.", "es").Value;
        topic.RegisterRequest(); var result = topic.Discard(1, Guid.NewGuid(), AtUtc);
        Assert.True(result.IsFailure); Assert.Equal(FormMissingRequestErrors.RevisionConflict, result.Error);
        Assert.Equal(BusinessFormMissingTopicState.Pending, topic.State); Assert.Null(topic.DiscardedAtUtc);
    }
    [Fact]
    public void A_request_keeps_only_opaque_identity_and_the_original_utc_instant()
    {
        var receipt = Guid.NewGuid(); var topic = Guid.NewGuid(); var subject = Guid.NewGuid();
        var result = BusinessFormMissingRequest.Create(receipt, topic, subject, AtUtc);
        Assert.True(result.IsSuccess); Assert.Equal(receipt, result.Value.SourceReceiptId); Assert.Equal(topic, result.Value.TopicId);
        Assert.Equal(subject, result.Value.SubjectUserId); Assert.Equal(AtUtc, result.Value.RequestedAtUtc);
        Assert.DoesNotContain(receipt.ToString(), result.Value.ToString(), StringComparison.Ordinal);
    }
    [Fact]
    public void A_request_rejects_missing_identities_and_a_non_utc_instant()
    {
        Assert.True(BusinessFormMissingRequest.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), AtUtc).IsFailure);
        Assert.True(BusinessFormMissingRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.SpecifyKind(AtUtc, DateTimeKind.Unspecified)).IsFailure);
    }
}
